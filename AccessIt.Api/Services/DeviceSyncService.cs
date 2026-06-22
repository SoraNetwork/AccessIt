using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

public interface IDeviceSyncService
{
    Task<SyncRun> SyncAsync(Guid deviceId, string actorUserId, CancellationToken cancellationToken = default);
    Task ResolveAsync(Guid conflictId, SyncConflictResolution resolution, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class DeviceSyncService(AccessItDbContext db, IHikiotGateway hikiot, IIssuanceJobService jobs, IAuditService audit) : IDeviceSyncService
{
    public async Task<SyncRun> SyncAsync(Guid deviceId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var device = await db.AccessDevices.FindAsync([deviceId], cancellationToken) ?? throw new KeyNotFoundException("Device was not found.");
        var run = new SyncRun { AccessDeviceId = deviceId, StartedByUserId = actorUserId };
        db.SyncRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var page = 1;
            var remotePeople = new List<HikiotRemotePerson>();
            while (true)
            {
                var result = await hikiot.SearchPeopleAsync(device.DeviceSerial, page, 20, null, cancellationToken);
                if (!result.Succeeded) throw new InvalidOperationException($"设备人员查询失败：{result.Message}");
                remotePeople.AddRange(result.People);
                if (remotePeople.Count >= result.Count || result.People.Count == 0) break;
                page++;
            }

            var local = await db.AccessPeople.ToDictionaryAsync(x => x.EmployeeNo, StringComparer.OrdinalIgnoreCase, cancellationToken);
            foreach (var remote in remotePeople)
            {
                if (!local.TryGetValue(remote.EmployeeNo, out var person))
                {
                    AddConflict(run, null, remote.EmployeeNo, "remoteRecord", null, $"{remote.Name}|{remote.UserType}");
                    run.NewCount++;
                    continue;
                }
                if (!string.Equals(person.Name, remote.Name, StringComparison.Ordinal))
                    AddConflict(run, person.Id, remote.EmployeeNo, "name", person.Name, remote.Name);
                if (person.PermanentValid != remote.PermanentValid)
                    AddConflict(run, person.Id, remote.EmployeeNo, "permanentValid", person.PermanentValid.ToString(), remote.PermanentValid.ToString());
                if (!person.PermanentValid && remote.EndTime is DateTime end && person.EnableEndTime != end)
                    AddConflict(run, person.Id, remote.EmployeeNo, "enableEndTime", person.EnableEndTime.ToString("O"), end.ToString("O"));
            }
            run.RemoteCount = remotePeople.Count;
            run.ConflictCount = db.ChangeTracker.Entries<SyncConflict>().Count(x => x.Entity.SyncRunId == run.Id);
            run.Status = SyncRunStatus.Completed;
            run.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(actorUserId, "device.sync.completed", "SyncRun", run.Id, new { device.DeviceSerial, run.RemoteCount, run.ConflictCount }, cancellationToken);
            return run;
        }
        catch (Exception ex)
        {
            run.Status = SyncRunStatus.Failed;
            run.FailureMessage = ex.Message;
            run.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task ResolveAsync(Guid conflictId, SyncConflictResolution resolution, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (resolution == SyncConflictResolution.Pending) throw new ArgumentOutOfRangeException(nameof(resolution));
        var conflict = await db.SyncConflicts.FindAsync([conflictId], cancellationToken) ?? throw new KeyNotFoundException("Sync conflict was not found.");
        if (conflict.Resolution != SyncConflictResolution.Pending) throw new InvalidOperationException("该冲突已处理。");
        if (conflict.AccessPersonId is Guid personId)
        {
            var person = await db.AccessPeople.Include(x => x.DeviceGrants).Include(x => x.Cards).Include(x => x.FaceAssets).SingleAsync(x => x.Id == personId, cancellationToken);
            if (resolution == SyncConflictResolution.KeepLocal)
            {
                var run = await db.SyncRuns.FindAsync([conflict.SyncRunId], cancellationToken);
                if (run is not null)
                {
                    var granted = await db.DeviceGrants.Where(x => x.AccessPersonId == person.Id && x.AccessDeviceId == run.AccessDeviceId).Select(x => x.AccessDevice).ToListAsync(cancellationToken);
                    await jobs.QueueUpsertAsync(person, granted, actorUserId, cancellationToken);
                }
            }
            else
            {
                ApplyRemoteValue(person, conflict.FieldName, conflict.RemoteValue);
            }
        }
        conflict.Resolution = resolution;
        conflict.ResolvedByUserId = actorUserId;
        conflict.ResolvedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "device.sync.conflict.resolved", "SyncConflict", conflict.Id, new { resolution }, cancellationToken);
    }

    private void AddConflict(SyncRun run, Guid? personId, string employeeNo, string field, string? local, string? remote)
    {
        db.SyncConflicts.Add(new SyncConflict { SyncRunId = run.Id, AccessPersonId = personId, EmployeeNo = employeeNo, FieldName = field, LocalValue = local, RemoteValue = remote });
    }

    private static void ApplyRemoteValue(AccessPerson person, string field, string? value)
    {
        switch (field)
        {
            case "name" when !string.IsNullOrWhiteSpace(value): person.Name = value; break;
            case "permanentValid" when bool.TryParse(value, out var permanent): person.PermanentValid = permanent; break;
            case "enableEndTime" when DateTime.TryParse(value, CultureInfo.InvariantCulture, out var end): person.EnableEndTime = end; break;
        }
        person.UpdatedAtUtc = DateTime.UtcNow;
    }
}
