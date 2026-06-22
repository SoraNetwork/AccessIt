using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Security;

namespace AccessIt.Api.Services;

public sealed record CreateEmployeeInput(string Name, string? DingTalkUserId, string? Mobile, IReadOnlyList<Guid> DeviceIds);
public sealed record CreateVisitorInput(string Name, DateTime BeginTime, DateTime EndTime, int MaxOpenDoorTime, string? Mobile, IReadOnlyList<Guid> DeviceIds);
public sealed record UpdateVisitorInput(DateTime BeginTime, DateTime EndTime, int MaxOpenDoorTime, string? Mobile, IReadOnlyList<Guid> DeviceIds);

public interface IPersonService
{
    Task<AccessPerson> CreateEmployeeAsync(CreateEmployeeInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessPerson> CreateVisitorAsync(CreateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessPerson> UpdateVisitorAsync(Guid personId, UpdateVisitorInput input, string actorUserId, CancellationToken cancellationToken = default);
    Task<AccessCard> AddCardAsync(Guid personId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default);
    Task SetPasswordAsync(Guid personId, string password, string actorUserId, CancellationToken cancellationToken = default);
    Task<FaceAsset> AddFaceAsync(Guid personId, Stream image, string actorUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class PersonService(
    AccessItDbContext db,
    IIssuanceJobService jobs,
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
        var devices = await AssignDevicesAsync(person, input.DeviceIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.employee.created", "AccessPerson", person.Id, new { person.EmployeeNo }, cancellationToken);
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

    public async Task<AccessCard> AddCardAsync(Guid personId, string cardNo, bool isVirtual, string actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardNo);
        var person = await LoadPersonAsync(personId, cancellationToken);
        if (await db.AccessCards.AnyAsync(x => x.CardNo == cardNo, cancellationToken)) throw new InvalidOperationException("该卡号已被使用。");
        var card = new AccessCard { AccessPersonId = personId, CardNo = cardNo.Trim(), IsVirtual = isVirtual };
        db.AccessCards.Add(card);
        person.Cards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        var devices = await ActiveDevicesAsync(personId, cancellationToken);
        await jobs.QueueUpsertAsync(person, devices, actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.card.added", "AccessCard", card.Id, new { person.EmployeeNo, card.CardNo }, cancellationToken);
        return card;
    }

    public async Task SetPasswordAsync(Guid personId, string password, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (password.Length is < 4 or > 6) throw new ArgumentOutOfRangeException(nameof(password), "密码长度必须为 4 到 6 位。");
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
        await jobs.QueueUpsertAsync(person, await ActiveDevicesAsync(personId, cancellationToken), actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.password.updated", "AccessPerson", personId, null, cancellationToken);
    }

    public async Task<FaceAsset> AddFaceAsync(Guid personId, Stream image, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await LoadPersonAsync(personId, cancellationToken);
        foreach (var existing in person.FaceAssets.ToList()) await faceStorage.DeleteAsync(existing, cancellationToken);
        var face = await faceStorage.StoreAsync(person, image, cancellationToken);
        person.FaceAssets.Add(face);
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueUpsertAsync(person, await ActiveDevicesAsync(personId, cancellationToken), actorUserId, cancellationToken);
        await audit.WriteAsync(actorUserId, "person.face.updated", "FaceAsset", face.Id, new { person.EmployeeNo }, cancellationToken);
        return face;
    }

    public async Task DeleteAsync(Guid personId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var person = await LoadPersonAsync(personId, cancellationToken);
        person.Status = PersonStatus.Deleted;
        await db.SaveChangesAsync(cancellationToken);
        await jobs.QueueDeleteAsync(person, await ActiveDevicesAsync(personId, cancellationToken), actorUserId, cancellationToken);
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
