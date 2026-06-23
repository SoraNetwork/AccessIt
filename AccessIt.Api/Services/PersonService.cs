using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;
using AccessIt.Api.Security;

namespace AccessIt.Api.Services;

public sealed record CreateEmployeeInput(string Name, string? DingTalkUserId, string? Mobile, IReadOnlyList<Guid> DeviceIds);
public sealed record CreateVisitorInput(string Name, DateTime BeginTime, DateTime EndTime, int MaxOpenDoorTime, string? Mobile, IReadOnlyList<Guid> DeviceIds);
public sealed record UpdateVisitorInput(DateTime BeginTime, DateTime EndTime, int MaxOpenDoorTime, string? Mobile, IReadOnlyList<Guid> DeviceIds);
public sealed record UpdateEmployeeInput(string Name, string? DingTalkUserId, string? Mobile);

public interface IPersonService
{
    Task<AccessPerson> CreateEmployeeAsync(CreateEmployeeInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessPerson> CreateVisitorAsync(CreateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessPerson> UpdateVisitorAsync(Guid personId, UpdateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessPerson> UpdateEmployeeAsync(Guid personId, UpdateEmployeeInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessCard> AddCardAsync(Guid personId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessCard> UpdateCardAsync(Guid personId, Guid cardId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid personId, string password, string actorUserId, CancellationToken cancellationToken = default);
    Task<FaceAsset> AddFaceAsync(Guid personId, Stream image, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class PersonService(
    AccessItDbContext db,
    IIssuanceJobService jobs,
    IHikiotTeamPeopleService teamPeople,
    IStandardAuthorityIssuanceService authorityIssuance,
    IHikiotGateway hikiot,
    ISecretProtector secretProtector,
    IFaceStorageService faceStorage,
    IAuditService audit) : IPersonService
{
    public async Task<AccessPerson> CreateEmployeeAsync(CreateEmployeeInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var sequence = await NextSequenceAsync(PersonKind.Employee, cancellationToken);
        var person = AccessPerson.CreateEmployee(sequence, input.Name, input.DingTalkUserId);
        person.Mobile = input.Mobile;
        db.AccessPeople.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        // Employee changes are published immediately: team member/credentials first, then the
        // standard authority-config and issued-job path. The input device list is intentionally
        // ignored because employee policy is all managed access devices.
        await teamPeople.PublishAsync(person.Id, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.employee.created", "AccessPerson", person.Id, new { person.EmployeeNo, automaticallyPublished = true }, cancellationToken);
        return person;
    }

    public async Task<AccessPerson> CreateVisitorAsync(CreateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var sequence = await NextSequenceAsync(PersonKind.Visitor, cancellationToken);
        var person = AccessPerson.CreateVisitor(sequence, input.Name, input.BeginTime, input.EndTime, input.MaxOpenDoorTime);
        person.Mobile = input.Mobile;
        db.AccessPeople.Add(person);
        var devices = await AssignDevicesAsync(person, input.DeviceIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.visitor.created", "AccessPerson", person.Id, new { person.EmployeeNo }, cancellationToken);
        return person;
    }

    public async Task<AccessPerson> UpdateVisitorAsync(Guid personId, UpdateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.Include(x => x.DeviceGrants).Include(x => x.Cards).Include(x => x.FaceAssets).SingleOrDefaultAsync(x => x.Id == personId, cancellationToken)
                     ?? throw new KeyNotFoundException("Person was not found.");
        person.UpdateVisitorWindow(input.BeginTime, input.EndTime, input.MaxOpenDoorTime);
        person.Mobile = input.Mobile;
        var devices = await ReplaceDeviceAssignmentsAsync(person, input.DeviceIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.visitor.updated", "AccessPerson", person.Id, null, cancellationToken);
        return person;
    }

    public async Task<AccessPerson> UpdateEmployeeAsync(Guid personId, UpdateEmployeeInput input, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await LoadPersonAsync(personId, cancellationToken);
        if (person.Kind != PersonKind.Employee) throw new InvalidOperationException("Only employee details can be updated here.");
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Name);
        if (System.Text.Encoding.UTF8.GetByteCount(input.Name.Trim()) > 32)
            throw new ArgumentOutOfRangeException(nameof(input.Name), "Name must not exceed 32 UTF-8 bytes.");
        person.Name = input.Name.Trim();
        person.DingTalkUserId = string.IsNullOrWhiteSpace(input.DingTalkUserId) ? null : input.DingTalkUserId.Trim();
        person.Mobile = string.IsNullOrWhiteSpace(input.Mobile) ? null : input.Mobile.Trim();
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await QueueCurrentGrantsAsync(person, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.employee.updated", "AccessPerson", person.Id, new { person.EmployeeNo }, cancellationToken);
        return person;
    }

    public async Task<AccessCard> AddCardAsync(Guid personId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardNo);
        var person = await LoadPersonAsync(personId, cancellationToken);
        if (await db.AccessCards.AnyAsync(x => x.CardNo == cardNo, cancellationToken)) throw new InvalidOperationException("该卡号已被使用。");
        var card = new AccessCard { AccessPersonId = personId, CardNo = cardNo.Trim(), IsVirtual = isVirtual };
        db.AccessCards.Add(card);
        person.Cards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        await QueueCurrentGrantsAsync(person, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.card.added", "AccessCard", card.Id, new { person.EmployeeNo, card.CardNo }, cancellationToken);
        return card;
    }

    public async Task<AccessCard> UpdateCardAsync(Guid personId, Guid cardId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardNo);
        var person = await LoadPersonAsync(personId, cancellationToken);
        var card = person.Cards.SingleOrDefault(x => x.Id == cardId) ?? throw new KeyNotFoundException("Card was not found for this person.");
        var newCardNo = cardNo.Trim();
        if (await db.AccessCards.AnyAsync(x => x.CardNo == newCardNo && x.Id != cardId, cancellationToken))
            throw new InvalidOperationException("The card number is already assigned to another person.");
        var oldCardNo = card.CardNo;
        var numberChanged = !string.Equals(oldCardNo, newCardNo, StringComparison.Ordinal);
        card.CardNo = newCardNo;
        card.IsVirtual = isVirtual;
        // Keep the managed team-identification id for employee replacements so PublishAsync can
        // delete the old remote card before adding the new number. Visitors use device-direct cards.
        if (numberChanged && person.Kind != PersonKind.Employee) card.HikiotIdentificationId = null;
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (person.Kind == PersonKind.Employee)
            await teamPeople.PublishAsync(person.Id, actorUserId, cancellationToken);
        else
        {
            var devices = await ActiveDevicesAsync(personId, cancellationToken);
            if (numberChanged && devices.Count > 0)
                await jobs.QueueCardReplacementAsync(person, card, oldCardNo, devices, actorUserId, cancellationToken);
            else if (devices.Count > 0)
                await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        }
        await audit.WriteAsync(actorUserId, "person.card.updated", "AccessCard", card.Id, new { person.EmployeeNo, oldCardNo, card.CardNo, card.IsVirtual }, cancellationToken);
        return card;
    }

    public async Task SetPasswordAsync(Guid personId, string password, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (password.Length is < 4 or > 8) throw new ArgumentOutOfRangeException(nameof(password), "密码长度必须为 4 到 8 位。");
        var person = await LoadPersonAsync(personId, cancellationToken);
        var record = await db.DevicePasswords.SingleOrDefaultAsync(x => x.AccessPersonId == personId, cancellationToken);
        if (record is null)
        {
            record = new DevicePassword { AccessPersonId = personId };
            db.DevicePasswords.Add(record);
        }
        record.ProtectedValue = secretProtector.Protect(password);
        record.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await QueueCurrentGrantsAsync(person, actorUserId, cancellationToken);
        if (person.Kind == PersonKind.Employee)
            await DistributeEmployeePasswordAsync(person, password, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.password.updated", "AccessPerson", personId, null, cancellationToken);
    }

    public async Task<FaceAsset> AddFaceAsync(Guid personId, Stream image, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await LoadPersonAsync(personId, cancellationToken);
        foreach (var existing in person.FaceAssets.ToList()) await faceStorage.DeleteAsync(existing, cancellationToken);
        var face = await faceStorage.StoreAsync(person, image, cancellationToken);
        person.FaceAssets.Add(face);
        await db.SaveChangesAsync(cancellationToken);
        await QueueCurrentGrantsAsync(person, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.face.updated", "FaceAsset", face.Id, new { person.EmployeeNo }, cancellationToken);
        return face;
    }

    public async Task DeleteAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await LoadPersonAsync(personId, cancellationToken);
        person.Status = PersonStatus.Deleted;
        if (person.Kind == PersonKind.Visitor)
        {
            var now = DateTime.UtcNow;
            var shares = await db.VisitorQrShares.Where(x => x.AccessPersonId == personId && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var share in shares) share.RevokedAtUtc = now;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (person.Kind == PersonKind.Employee)
        {
            await authorityIssuance.RevokeEmployeeAsync(person, actorUserId, cancellationToken);
            // Then delete device-level credentials
            var devices = await db.AccessDevices.Where(x => x.IsManaged && x.SupportsUserInfo).ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                foreach (var face in person.FaceAssets) await hikiot.DeleteFaceAsync(device.DeviceSerial, person.EmployeeNo, cancellationToken);
                foreach (var card in person.Cards.Where(x => !x.IsVirtual)) await hikiot.DeleteCardAsync(device.DeviceSerial, card.CardNo, cancellationToken);
                await hikiot.DeleteUserAsync(device.DeviceSerial, person.EmployeeNo, cancellationToken);
            }
        }
        else
        {
            await jobs.QueueDeleteAsync(person, await ActiveDevicesAsync(personId, cancellationToken), actorUserId, cancellationToken);
        }
        await audit.WriteAsync(actorUserId, "person.delete.requested", "AccessPerson", personId, new { person.EmployeeNo }, cancellationToken);
    }

    private async Task<AccessPerson> LoadPersonAsync(Guid personId, CancellationToken cancellationToken)
        => await db.AccessPeople.Include(x => x.DeviceGrants).Include(x => x.Cards).Include(x => x.FaceAssets).SingleOrDefaultAsync(x => x.Id == personId, cancellationToken)
           ?? throw new KeyNotFoundException("Person was not found.");

    private async Task<List<AccessDevice>> AssignDevicesAsync(AccessPerson person, IReadOnlyList<Guid> deviceIds, CancellationToken cancellationToken)
    {
        var devices = await db.AccessDevices.Where(x => deviceIds.Contains(x.Id) && x.IsManaged).ToListAsync(cancellationToken);
        if (devices.Count != deviceIds.Distinct().Count()) throw new InvalidOperationException("至少一个设备不存在或未纳管。");
        foreach (var device in devices) person.DeviceGrants.Add(new DeviceGrant { AccessDeviceId = device.Id });
        return devices;
    }

    private async Task<List<AccessDevice>> ReplaceDeviceAssignmentsAsync(AccessPerson person, IReadOnlyList<Guid> deviceIds, CancellationToken cancellationToken)
    {
        var devices = await db.AccessDevices.Where(x => deviceIds.Contains(x.Id) && x.IsManaged).ToListAsync(cancellationToken);
        if (devices.Count != deviceIds.Distinct().Count()) throw new InvalidOperationException("至少一个设备不存在或未纳管。");
        db.DeviceGrants.RemoveRange(person.DeviceGrants);
        person.DeviceGrants.Clear();
        foreach (var device in devices) person.DeviceGrants.Add(new DeviceGrant { AccessDeviceId = device.Id });
        return devices;
    }

    private Task<List<AccessDevice>> ActiveDevicesAsync(Guid personId, CancellationToken cancellationToken)
        => db.DeviceGrants.Where(x => x.AccessPersonId == personId && x.IsActive).Select(x => x.AccessDevice).ToListAsync(cancellationToken);

    private async Task QueueCurrentGrantsAsync(AccessPerson person, string actorUserId, CancellationToken cancellationToken)
    {
        if (person.Kind == PersonKind.Employee)
        {
            await teamPeople.PublishAsync(person.Id, actorUserId, cancellationToken);
            return;
        }
        var devices = await ActiveDevicesAsync(person.Id, cancellationToken);
        if (devices.Count > 0) await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
    }

    private async Task DistributeEmployeePasswordAsync(AccessPerson person, string password, string actorUserId, CancellationToken cancellationToken)
    {
        var devices = await ActiveDevicesAsync(person.Id, cancellationToken);
        var targetDevices = devices.Where(x => x.IsManaged && x.SupportsUserInfo && x.SupportsPurePassword).DistinctBy(x => x.Id).ToList();
        var traces = new List<object>();
        var failures = new List<string>();
        foreach (var device in targetDevices)
        {
            if (device.SupportsUserRightPlanTemplate && (device.AllDayTemplateId == null || !device.HasAllDayTemplate))
            {
                var template = await hikiot.EnsureAllDayTemplateAsync(device.DeviceSerial, cancellationToken);
                if (!template.Succeeded)
                {
                    failures.Add($"{device.DeviceSerial}: unable to initialize access template ({template.Code} {template.Message})");
                    continue;
                }
                device.HasAllDayTemplate = true;
                device.AllDayTemplateId = 8;
            }

            // The documented password channel is the device-direct user upsert. It is deliberately
            // limited to pure-password-capable devices and does not write a password to the team.
            var result = await hikiot.UpsertUserAsync(device.DeviceSerial, person, password, device.SupportsUserRightPlanTemplate ? device.AllDayTemplateId : null, cancellationToken);
            if (!result.Succeeded)
            {
                failures.Add($"{device.DeviceSerial}: {result.Code} {result.Message}{(string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : $": {result.Detail}")}");
                continue;
            }
            traces.Add(new { device.DeviceSerial, result.TraceId });
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "person.password.device-issued", "AccessPerson", person.Id, new
        {
            person.EmployeeNo,
            targetedDeviceCount = targetDevices.Count,
            unsupportedDeviceCount = devices.Count - targetDevices.Count,
            traces,
            failures
        }, cancellationToken);
        if (failures.Count > 0)
            throw new InvalidOperationException($"Password was saved but could not be issued to every compatible device: {string.Join("; ", failures)}");
    }

    private async Task<long> NextSequenceAsync(PersonKind kind, CancellationToken cancellationToken)
    {
        var sequence = await db.PersonNumberSequences.FindAsync([kind], cancellationToken);
        if (sequence is null)
        {
            sequence = new PersonNumberSequence { Kind = kind, LastValue = 0 };
            db.PersonNumberSequences.Add(sequence);
        }
        sequence.LastValue++;
        return sequence.LastValue;
    }
}
