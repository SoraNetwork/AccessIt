using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

public sealed record SourceSyncCounts(int Read, int Created, int Updated, int Skipped, int Conflicts);
public sealed record ExternalPeopleSyncResult(SourceSyncCounts Hikiot, SourceSyncCounts DingTalk, DirectorySyncResult Directory);
public sealed record HikiotTeamPublishResult(string PersonNo, bool CreatedInTeam, int DeviceCount, int CardCount, bool FacePublished);

public interface IHikiotTeamPeopleService
{
    Task<ExternalPeopleSyncResult> SyncSourcesAsync(string actorUserId, CancellationToken cancellationToken = default);
    Task<HikiotTeamPublishResult> PublishAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default);
    Task RemoveFromTeamAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class HikiotTeamPeopleService(
    AccessItDbContext db,
    IHikiotGateway hikiot,
    IDingTalkGateway dingTalk,
    IIdentityService identity,
    IIssuanceJobService jobs,
    IAuditService audit) : IHikiotTeamPeopleService
{
    private static readonly Regex MainlandPhone = new("^1[3-9]\\d{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ExternalPeopleSyncResult> SyncSourcesAsync(string actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await db.AccessPeople.Include(x => x.Cards).Where(x => x.Kind == PersonKind.Employee).ToListAsync(cancellationToken);
        var cardOwners = existing.SelectMany(x => x.Cards).ToDictionary(x => x.CardNo, x => x.AccessPersonId, StringComparer.Ordinal);
        var hikiotPeople = await hikiot.GetTeamPeopleAsync(cancellationToken);
        var hikiotCounts = new MutableCounts { Read = hikiotPeople.Count };

        foreach (var remote in hikiotPeople)
        {
            // The page API omits fields on some HIKIoT tenants (notably job position and ID card), so enrich it from the detail API.
            var profile = await hikiot.GetTeamPersonAsync(remote.PersonNo, cancellationToken) ?? remote;
            var person = FindByHikiotOrName(existing, profile.PersonNo, profile.Name, hikiotCounts);
            if (person is null)
            {
                if (HasAmbiguousName(existing, profile.Name)) { hikiotCounts.Conflicts++; continue; }
                person = await CreateImportedEmployeeAsync(profile.Name, null, cancellationToken);
                existing.Add(person);
                hikiotCounts.Created++;
            }
            else hikiotCounts.Updated++;

            ApplyHikiot(person, profile);
            var identifications = await hikiot.GetTeamIdentificationsAsync(profile.PersonNo, cancellationToken);
            foreach (var card in identifications.Where(x => x.Type == HikiotIdentificationType.Card && !string.IsNullOrWhiteSpace(x.Content)))
            {
                var normalized = card.Content.Trim();
                if (cardOwners.TryGetValue(normalized, out var ownerId) && ownerId != person.Id) { hikiotCounts.Conflicts++; continue; }
                var localCard = person.Cards.SingleOrDefault(x => x.CardNo == normalized);
                if (localCard is null)
                {
                    localCard = new AccessCard { AccessPersonId = person.Id, CardNo = normalized, IsVirtual = false, HikiotIdentificationId = card.Id };
                    person.Cards.Add(localCard);
                    cardOwners[normalized] = person.Id;
                }
                else localCard.HikiotIdentificationId = card.Id;
            }
            person.HikiotFaceIdentificationId = identifications.Where(x => x.Type == HikiotIdentificationType.FaceUrl).Select(x => (long?)x.Id).FirstOrDefault();
            person.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        var dingTalkPeople = await dingTalk.GetDirectoryAsync(cancellationToken);
        var directory = await identity.SyncDirectoryAsync(dingTalkPeople, cancellationToken);
        var dingTalkCounts = new MutableCounts { Read = dingTalkPeople.Count };
        foreach (var remote in dingTalkPeople.Where(x => !string.IsNullOrWhiteSpace(x.UserId) && !string.IsNullOrWhiteSpace(x.Name)))
        {
            var person = FindByDingTalkOrName(existing, remote.UserId, remote.Name, dingTalkCounts);
            if (person is null)
            {
                if (HasAmbiguousName(existing, remote.Name)) { dingTalkCounts.Conflicts++; continue; }
                person = await CreateImportedEmployeeAsync(remote.Name, remote.UserId, cancellationToken);
                existing.Add(person);
                dingTalkCounts.Created++;
            }
            else dingTalkCounts.Updated++;

            person.DingTalkUserId = remote.UserId.Trim();
            person.Name = remote.Name.Trim();
            person.Mobile = NullIfWhiteSpace(remote.Mobile);
            person.Status = remote.IsActive ? PersonStatus.Active : PersonStatus.Disabled;
            person.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "people.sources.synced", "AccessPeople", "sources", new
        {
            hikiot = hikiotCounts.ToImmutable(), dingTalk = dingTalkCounts.ToImmutable(), directory
        }, cancellationToken);
        return new ExternalPeopleSyncResult(hikiotCounts.ToImmutable(), dingTalkCounts.ToImmutable(), directory);
    }

    public async Task<HikiotTeamPublishResult> PublishAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.Include(x => x.Cards).Include(x => x.FaceAssets).Include(x => x.DeviceGrants)
            .SingleOrDefaultAsync(x => x.Id == personId, cancellationToken)
            ?? throw new KeyNotFoundException("Person was not found.");
        if (person.Kind != PersonKind.Employee) throw new InvalidOperationException("Only employees can be published to the HIKIoT team.");
        if (person.Status != PersonStatus.Active) throw new InvalidOperationException("Only active employees can be published to the HIKIoT team.");
        if (!MainlandPhone.IsMatch(person.Mobile ?? string.Empty)) throw new InvalidOperationException("A valid 11-digit mainland China mobile number is required before publishing to HIKIoT.");

        var connection = await hikiot.GetConnectionStatusAsync(cancellationToken);
        if (!connection.IsAuthorized) throw new InvalidOperationException("HIKIoT authorization is missing or expired. Reauthorize it in System Settings first.");
        if (string.IsNullOrWhiteSpace(connection.DefaultDepartmentNo)) throw new InvalidOperationException("Select a default HIKIoT team department in System Settings before publishing.");

        var created = false;
        if (string.IsNullOrWhiteSpace(person.HikiotPersonNo))
        {
            var createdResult = await hikiot.CreateTeamPersonAsync(new HikiotTeamPersonUpsert(null, person.Name, connection.DefaultDepartmentNo, person.Mobile!, person.EmployeeNo, person.HikiotJobPosition, person.HikiotSex), cancellationToken);
            if (!createdResult.Succeeded || string.IsNullOrWhiteSpace(createdResult.PersonNo)) throw new InvalidOperationException($"Unable to create HIKIoT team member: {createdResult.Code} {createdResult.Message}");
            person.HikiotPersonNo = createdResult.PersonNo;
            person.HikiotDepartmentNo = connection.DefaultDepartmentNo;
            person.HikiotJobNumber = person.EmployeeNo;
            created = true;
        }
        else
        {
            var update = await hikiot.UpdateTeamPersonAsync(new HikiotTeamPersonUpsert(person.HikiotPersonNo, person.Name, person.HikiotDepartmentNo ?? connection.DefaultDepartmentNo, person.Mobile!, person.HikiotJobNumber ?? person.EmployeeNo, person.HikiotJobPosition, person.HikiotSex), cancellationToken);
            if (!update.Succeeded) throw new InvalidOperationException($"Unable to update HIKIoT team member: {update.Code} {update.Message}");
        }

        var teamPersonNo = person.HikiotPersonNo!;
        var remoteIdentifications = await hikiot.GetTeamIdentificationsAsync(teamPersonNo, cancellationToken);
        var cards = person.Cards.Where(x => !x.IsVirtual).OrderBy(x => x.CreatedAtUtc).ToList();
        if (cards.Count > 4) throw new InvalidOperationException("HIKIoT supports at most four physical cards per team member.");
        foreach (var card in cards)
        {
            var remote = remoteIdentifications.SingleOrDefault(x => x.Type == HikiotIdentificationType.Card && x.Content == card.CardNo);
            if (remote is not null) { card.HikiotIdentificationId = remote.Id; continue; }
            var added = await hikiot.AddTeamIdentificationAsync(teamPersonNo, HikiotIdentificationType.Card, card.CardNo, cancellationToken);
            if (!added.Succeeded) throw new InvalidOperationException($"Unable to publish card {card.CardNo}: {added.Code} {added.Message}");
            card.HikiotIdentificationId = added.IdentificationId;
        }

        var localFace = person.FaceAssets.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        if (localFace is not null)
        {
            foreach (var remoteFace in remoteIdentifications.Where(x => x.Type == HikiotIdentificationType.FaceUrl))
            {
                var removed = await hikiot.DeleteTeamIdentificationAsync(remoteFace.Id, cancellationToken);
                if (!removed.Succeeded) throw new InvalidOperationException($"Unable to replace the existing HIKIoT face: {removed.Code} {removed.Message}");
            }
            var faceAdded = await hikiot.AddTeamFaceAsync(teamPersonNo, localFace, cancellationToken);
            if (!faceAdded.Succeeded) throw new InvalidOperationException($"Unable to publish face: {faceAdded.Code} {faceAdded.Message}");
            localFace.HikiotIdentificationId = faceAdded.IdentificationId;
            person.HikiotFaceIdentificationId = faceAdded.IdentificationId;
        }

        var devices = await db.AccessDevices.Where(x => x.IsManaged && x.SupportsUserInfo).ToListAsync(cancellationToken);
        var grantsByDevice = person.DeviceGrants.ToDictionary(x => x.AccessDeviceId);
        foreach (var device in devices)
        {
            if (grantsByDevice.TryGetValue(device.Id, out var grant)) grant.IsActive = true;
            else person.DeviceGrants.Add(new DeviceGrant { AccessDeviceId = device.Id, IsActive = true });
        }
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.hikiot.published", "AccessPerson", person.Id, new { person.EmployeeNo, teamPersonNo, created, deviceCount = devices.Count, cardCount = cards.Count, hasFace = localFace is not null }, cancellationToken);
        return new HikiotTeamPublishResult(teamPersonNo, created, devices.Count, cards.Count, localFace is not null);
    }

    public async Task RemoveFromTeamAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.SingleOrDefaultAsync(x => x.Id == personId, cancellationToken) ?? throw new KeyNotFoundException("Person was not found.");
        if (person.Kind != PersonKind.Employee) throw new InvalidOperationException("Visitors are never stored in the HIKIoT team.");
        if (string.IsNullOrWhiteSpace(person.HikiotPersonNo)) throw new InvalidOperationException("This employee has not been published to the HIKIoT team.");
        var personNo = person.HikiotPersonNo;
        var result = await hikiot.RemoveTeamPersonAsync(personNo, cancellationToken);
        if (!result.Succeeded) throw new InvalidOperationException($"Unable to remove HIKIoT team member: {result.Code} {result.Message}");
        person.HikiotPersonNo = null;
        person.HikiotDepartmentNo = null;
        person.HikiotJobNumber = null;
        person.HikiotJobPosition = null;
        person.HikiotSex = null;
        person.HikiotFaceIdentificationId = null;
        foreach (var card in await db.AccessCards.Where(x => x.AccessPersonId == person.Id).ToListAsync(cancellationToken)) card.HikiotIdentificationId = null;
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "person.hikiot.removed", "AccessPerson", person.Id, new { person.EmployeeNo, personNo }, cancellationToken);
    }

    private async Task<AccessPerson> CreateImportedEmployeeAsync(string name, string? dingTalkUserId, CancellationToken cancellationToken)
    {
        var sequence = await NextEmployeeSequenceAsync(cancellationToken);
        var person = AccessPerson.CreateEmployee(sequence, name, dingTalkUserId);
        db.AccessPeople.Add(person);
        return person;
    }

    private static AccessPerson? FindByHikiotOrName(List<AccessPerson> people, string personNo, string name, MutableCounts counts)
    {
        var byId = people.Where(x => x.HikiotPersonNo == personNo).ToList();
        if (byId.Count == 1) return byId[0];
        if (byId.Count > 1) { counts.Conflicts++; return null; }
        var byName = people.Where(x => SameName(x.Name, name)).ToList();
        if (byName.Count == 1) return byName[0];
        if (byName.Count > 1) counts.Conflicts++;
        return null;
    }

    private static AccessPerson? FindByDingTalkOrName(List<AccessPerson> people, string userId, string name, MutableCounts counts)
    {
        var byId = people.Where(x => x.DingTalkUserId == userId).ToList();
        if (byId.Count == 1) return byId[0];
        if (byId.Count > 1) { counts.Conflicts++; return null; }
        var byName = people.Where(x => SameName(x.Name, name)).ToList();
        if (byName.Count == 1) return byName[0];
        if (byName.Count > 1) counts.Conflicts++;
        return null;
    }

    private static bool HasAmbiguousName(List<AccessPerson> people, string name) => people.Count(x => SameName(x.Name, name)) > 1;
    private static bool SameName(string left, string right) => NormalizeName(left) == NormalizeName(right);
    private static string NormalizeName(string name) => string.Concat((name ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ApplyHikiot(AccessPerson person, HikiotTeamPerson remote)
    {
        person.HikiotPersonNo = remote.PersonNo;
        person.HikiotDepartmentNo = NullIfWhiteSpace(remote.DepartmentNo);
        person.HikiotJobNumber = NullIfWhiteSpace(remote.JobNumber);
        person.HikiotJobPosition = NullIfWhiteSpace(remote.JobPosition);
        person.HikiotSex = remote.Sex;
        person.Name = remote.Name.Trim();
        if (!IsMaskedPhone(remote.Phone)) person.Mobile = NullIfWhiteSpace(remote.Phone);
    }

    private static bool IsMaskedPhone(string? phone) => string.IsNullOrWhiteSpace(phone) || phone.Contains('*');

    private async Task<long> NextEmployeeSequenceAsync(CancellationToken cancellationToken)
    {
        var sequence = await db.PersonNumberSequences.FindAsync([PersonKind.Employee], cancellationToken);
        if (sequence is null) { sequence = new PersonNumberSequence { Kind = PersonKind.Employee }; db.PersonNumberSequences.Add(sequence); }
        sequence.LastValue++;
        return sequence.LastValue;
    }

    private sealed class MutableCounts
    {
        public int Read { get; init; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Conflicts { get; set; }
        public SourceSyncCounts ToImmutable() => new(Read, Created, Updated, Skipped, Conflicts);
    }
}
