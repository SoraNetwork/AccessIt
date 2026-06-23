using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

/// <summary>
/// Maintains only the authority configurations owned by AccessIt.  Employee permissions are
/// deliberately issued through HIKIoT's authority-config and issued-job APIs; direct-device
/// commands remain reserved for visitor credentials and device-only passwords.
/// </summary>
public sealed record HikiotAuthorityIssueFailure(string? DeviceSerial, string Operation, int? Code, string Message);
public sealed record HikiotAuthorityIssueResult(int DeviceCount, int ConfiguredCount, int SubmittedCount, int ConfirmedCount, int PendingCount, IReadOnlyList<HikiotAuthorityIssueFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0 && PendingCount == 0;
}

public interface IStandardAuthorityIssuanceService
{
    Task<HikiotAuthorityIssueResult> PublishEmployeeAsync(AccessPerson person, IReadOnlyCollection<AccessDevice> devices, string actorUserId, CancellationToken cancellationToken = default);
    Task<HikiotAuthorityIssueResult> RevokeEmployeeAsync(AccessPerson person, string actorUserId, CancellationToken cancellationToken = default);
    Task<int> ReconcilePendingAsync(CancellationToken cancellationToken = default);
}

public sealed class StandardAuthorityIssuanceService(
    AccessItDbContext db,
    IHikiotGateway hikiot,
    IAuditService audit,
    TimeProvider timeProvider) : IStandardAuthorityIssuanceService
{
    private const int HikiotSuccessStatus = 2;
    private const int HikiotDeleteStatus = 3;

    public async Task<HikiotAuthorityIssueResult> PublishEmployeeAsync(AccessPerson person, IReadOnlyCollection<AccessDevice> devices, string actorUserId, CancellationToken cancellationToken = default)
    {
        ValidateEmployee(person);
        var targets = devices.Where(x => x.IsManaged && x.SupportsUserInfo).DistinctBy(x => x.Id).ToList();
        var grants = person.DeviceGrants.ToDictionary(x => x.AccessDeviceId);
        var failures = new List<HikiotAuthorityIssueFailure>();
        var configured = 0;

        foreach (var device in targets)
        {
            if (!grants.TryGetValue(device.Id, out var grant))
            {
                grant = new DeviceGrant { AccessDeviceId = device.Id, IsActive = true };
                person.DeviceGrants.Add(grant);
                grants[device.Id] = grant;
            }

            var config = await hikiot.SaveAuthorityConfigAsync(new HikiotAuthorityConfigRequest(
                grant.HikiotAuthorityConfigId,
                BuildConfigName(person, device),
                $"AccessIt managed permission for {person.EmployeeNo}",
                person.HikiotPersonNo!,
                device.DeviceSerial), cancellationToken);
            if (!config.Succeeded || string.IsNullOrWhiteSpace(config.ConfigId))
            {
                var message = $"{config.Message}{(string.IsNullOrWhiteSpace(config.Detail) ? string.Empty : $": {config.Detail}")}";
                grant.HikiotLastFailedReason = message;
                grant.HikiotStatusCheckedAtUtc = UtcNow;
                failures.Add(new HikiotAuthorityIssueFailure(device.DeviceSerial, "SaveAuthorityConfig", config.Code, message));
                continue;
            }

            grant.IsActive = true;
            grant.HikiotAuthorityConfigId = config.ConfigId;
            grant.HikiotLastFailedReason = null;
            configured++;
        }

        await db.SaveChangesAsync(cancellationToken);
        var issue = await SubmitAndConfirmAsync(person, targets, grants, failures, false, cancellationToken);
        await audit.WriteAsync(actorUserId, "hikiot.authority.published", "AccessPerson", person.Id, new
        {
            person.EmployeeNo,
            person.HikiotPersonNo,
            issue.DeviceCount,
            issue.ConfiguredCount,
            issue.SubmittedCount,
            issue.ConfirmedCount,
            issue.PendingCount,
            failures = issue.Failures
        }, cancellationToken);
        return issue with { ConfiguredCount = configured };
    }

    public async Task<HikiotAuthorityIssueResult> RevokeEmployeeAsync(AccessPerson person, string actorUserId, CancellationToken cancellationToken = default)
    {
        if (person.Kind != PersonKind.Employee || string.IsNullOrWhiteSpace(person.HikiotPersonNo))
            return new HikiotAuthorityIssueResult(0, 0, 0, 0, 0, []);

        var grants = person.DeviceGrants.Where(x => x.IsActive).ToDictionary(x => x.AccessDeviceId);
        if (grants.Count == 0) return new HikiotAuthorityIssueResult(0, 0, 0, 0, 0, []);
        var devices = await db.AccessDevices.Where(x => grants.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        var failures = new List<HikiotAuthorityIssueFailure>();

        foreach (var (deviceId, grant) in grants)
        {
            var device = devices.SingleOrDefault(x => x.Id == deviceId);
            if (device is null) continue;
            if (string.IsNullOrWhiteSpace(grant.HikiotAuthorityConfigId))
            {
                // A legacy/direct grant has no AccessIt authority configuration to remove.
                grant.IsActive = false;
                continue;
            }

            var removed = await hikiot.DeleteAuthorityConfigAsync(grant.HikiotAuthorityConfigId, cancellationToken);
            if (!removed.Succeeded)
            {
                var message = $"{removed.Message}{(string.IsNullOrWhiteSpace(removed.Detail) ? string.Empty : $": {removed.Detail}")}";
                grant.HikiotLastFailedReason = message;
                grant.HikiotStatusCheckedAtUtc = UtcNow;
                failures.Add(new HikiotAuthorityIssueFailure(device.DeviceSerial, "DeleteAuthorityConfig", removed.Code, message));
                continue;
            }
            grant.IsActive = false;
            grant.HikiotLastFailedReason = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        var issue = await SubmitAndConfirmAsync(person, devices, grants, failures, true, cancellationToken);
        await audit.WriteAsync(actorUserId, "hikiot.authority.revoked", "AccessPerson", person.Id, new
        {
            person.EmployeeNo,
            person.HikiotPersonNo,
            issue.DeviceCount,
            issue.SubmittedCount,
            issue.ConfirmedCount,
            issue.PendingCount,
            failures = issue.Failures
        }, cancellationToken);
        return issue;
    }

    public async Task<int> ReconcilePendingAsync(CancellationToken cancellationToken = default)
    {
        var batches = await db.HikiotIssueBatches
            .Where(x => x.Status == "Submitted" || x.Status == "Pending")
            .OrderBy(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        var updated = 0;
        foreach (var personBatches in batches.GroupBy(x => x.AccessPersonId))
        {
            var person = await db.AccessPeople.Include(x => x.DeviceGrants).SingleOrDefaultAsync(x => x.Id == personBatches.Key, cancellationToken);
            if (person is null || string.IsNullOrWhiteSpace(person.HikiotPersonNo))
            {
                foreach (var batch in personBatches) { batch.Status = "Failed"; batch.FailureReason = "The local employee or HIKIoT person mapping no longer exists."; batch.CheckedAtUtc = UtcNow; updated++; }
                continue;
            }

            var batchNos = personBatches.Select(x => x.BatchNo).ToHashSet(StringComparer.Ordinal);
            var grants = person.DeviceGrants.Where(x => x.HikiotIssueBatchNo is not null && batchNos.Contains(x.HikiotIssueBatchNo)).ToDictionary(x => x.AccessDeviceId);
            if (grants.Count == 0) continue;
            var devices = await db.AccessDevices.Where(x => grants.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
            var remote = await ReadPersonDevicesAsync(person.HikiotPersonNo, devices, cancellationToken);
            ApplyRemoteState(remote, devices, grants);
            foreach (var batch in personBatches)
            {
                var relatedGrants = grants.Values.Where(x => x.HikiotIssueBatchNo == batch.BatchNo).ToList();
                var relatedRemote = remote.Where(x => relatedGrants.Any(grant => grant.HikiotPersonDeviceId == x.Id)).ToList();
                var revoking = relatedGrants.All(x => !x.IsActive);
                var failure = relatedRemote.FirstOrDefault(HasFinalFailure);
                if (failure is not null)
                {
                    batch.Status = "Failed";
                    batch.FailureReason = GetFailureReason(failure);
                }
                else if (revoking ? relatedRemote.Count == 0 : relatedRemote.Count == relatedGrants.Count && relatedRemote.All(IsFullyIssued))
                {
                    batch.Status = "Succeeded";
                    batch.FailureReason = null;
                }
                else
                {
                    batch.Status = "Pending";
                }
                batch.CheckedAtUtc = UtcNow;
                updated++;
            }
        }
        if (updated > 0) await db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    private async Task<HikiotAuthorityIssueResult> SubmitAndConfirmAsync(
        AccessPerson person,
        IReadOnlyCollection<AccessDevice> devices,
        IReadOnlyDictionary<Guid, DeviceGrant> grants,
        List<HikiotAuthorityIssueFailure> failures,
        bool revoking,
        CancellationToken cancellationToken)
    {
        // Give HIKIoT a short moment to create the person-device mappings
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var remote = await ReadPersonDevicesAsync(person.HikiotPersonNo!, devices, cancellationToken);
        ApplyRemoteState(remote, devices, grants);
        await db.SaveChangesAsync(cancellationToken);

        var candidates = remote
            .Where(x => grants.Values.Any(grant => grant.HikiotPersonDeviceId == x.Id))
            .Where(x => !HasUnsupportedCredential(x))
            .Where(x => NeedsIssue(x, revoking))
            .ToList();
        foreach (var unsupported in remote.Where(x => grants.Values.Any(grant => grant.HikiotPersonDeviceId == x.Id) && HasUnsupportedCredential(x)))
        {
            failures.Add(new HikiotAuthorityIssueFailure(unsupported.DeviceSerial, "SelectIssue", null, "The device does not support this authority credential."));
        }

        var submitted = 0;
        var createdBatches = new List<HikiotIssueBatch>();
        foreach (var group in candidates.Chunk(10))
        {
            var selected = await hikiot.SelectIssueAsync(group.Select(x => x.Id).ToArray(), cancellationToken);
            // 120524 means HIKIoT found no new configuration to issue. It is not an error.
            if (!selected.Succeeded && selected.Code != 120524)
            {
                failures.Add(new HikiotAuthorityIssueFailure(null, "SelectIssue", selected.Code, selected.Message));
                continue;
            }
            submitted += group.Length;
            if (string.IsNullOrWhiteSpace(selected.BatchNo)) continue;

            var batch = new HikiotIssueBatch { BatchNo = selected.BatchNo, AccessPersonId = person.Id, Status = "Submitted" };
            db.HikiotIssueBatches.Add(batch);
            createdBatches.Add(batch);
            foreach (var item in group)
            {
                var grant = grants.Values.SingleOrDefault(x => x.HikiotPersonDeviceId == item.Id);
                if (grant is not null) grant.HikiotIssueBatchNo = selected.BatchNo;
            }
        }
        await db.SaveChangesAsync(cancellationToken);

        var confirmed = 0;
        var pending = 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0)
            {
                var delaySeconds = attempt switch
                {
                    1 => 2,
                    2 => 2,
                    3 => 2,
                    _ => 5
                };
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            remote = await ReadPersonDevicesAsync(person.HikiotPersonNo!, devices, cancellationToken);
            ApplyRemoteState(remote, devices, grants);
            await db.SaveChangesAsync(cancellationToken);

            var relevant = remote.Where(x => grants.Values.Any(grant => grant.HikiotPersonDeviceId == x.Id)).ToList();
            var deviceFailures = relevant.Where(HasFinalFailure).ToList();
            foreach (var failure in deviceFailures)
            {
                if (!failures.Any(x => x.DeviceSerial == failure.DeviceSerial && x.Operation == "IssueStatus"))
                    failures.Add(new HikiotAuthorityIssueFailure(failure.DeviceSerial, "IssueStatus", null, GetFailureReason(failure)!));
            }

            if (revoking)
            {
                // After a config is deleted HIKIoT may no longer return the pair; absence is successful revocation.
                pending = relevant.Count;
                confirmed = devices.Count - pending - deviceFailures.Count;
            }
            else
            {
                confirmed = relevant.Count(IsFullyIssued);
                pending = devices.Count - confirmed - deviceFailures.Count;
            }
            if (pending <= 0) break;
        }

        var status = failures.Count > 0 ? "Failed" : pending > 0 ? "Pending" : "Succeeded";
        foreach (var batch in createdBatches)
        {
            batch.Status = status;
            batch.FailureReason = failures.FirstOrDefault()?.Message;
            batch.CheckedAtUtc = UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new HikiotAuthorityIssueResult(devices.Count, 0, submitted, Math.Max(0, confirmed), Math.Max(0, pending), failures);
    }

    private async Task<List<HikiotPersonDevice>> ReadPersonDevicesAsync(string personNo, IReadOnlyCollection<AccessDevice> devices, CancellationToken cancellationToken)
    {
        var all = new List<HikiotPersonDevice>();
        for (var page = 1; ; page++)
        {
            var result = await hikiot.GetPersonDevicesAsync(personNo, devices.Select(x => x.DeviceSerial).ToArray(), page, 100, cancellationToken);
            all.AddRange(result);
            if (result.Count < 100) break;
        }
        return all;
    }

    private void ApplyRemoteState(IEnumerable<HikiotPersonDevice> remote, IReadOnlyCollection<AccessDevice> devices, IReadOnlyDictionary<Guid, DeviceGrant> grants)
    {
        var deviceIds = devices.ToDictionary(x => x.DeviceSerial, x => x.Id, StringComparer.Ordinal);
        foreach (var item in remote)
        {
            if (!deviceIds.TryGetValue(item.DeviceSerial, out var deviceId) || !grants.TryGetValue(deviceId, out var grant)) continue;
            grant.HikiotPersonDeviceId = item.Id;
            grant.HikiotInfoStatus = item.InfoStatus;
            grant.HikiotIsSupported = item.IsSupported;
            grant.HikiotIsSending = item.IsSending;
            grant.HikiotLastFailedReason = item.LastFailedReason;
            grant.HikiotStatusCheckedAtUtc = UtcNow;
        }
    }

    private static IEnumerable<HikiotCredentialIssueState> States(HikiotPersonDevice item)
        => item.CredentialStates.Count > 0
            ? item.CredentialStates
            : [new HikiotCredentialIssueState("person", item.InfoStatus, item.IsSupported, item.IsSending, item.LastFailedReason)];

    private static bool HasUnsupportedCredential(HikiotPersonDevice item) => States(item).Any(x => x.IsSupported is false);
    private static bool HasFinalFailure(HikiotPersonDevice item) => States(item).Any(x => !x.IsSending.GetValueOrDefault() && !string.IsNullOrWhiteSpace(x.LastFailedReason));
    private static string? GetFailureReason(HikiotPersonDevice item) => States(item).Select(x => x.LastFailedReason).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static bool IsFullyIssued(HikiotPersonDevice item) => States(item).Where(x => x.IsSupported is not false).All(x => x.InfoStatus == HikiotSuccessStatus && !x.IsSending.GetValueOrDefault() && string.IsNullOrWhiteSpace(x.LastFailedReason));
    private static bool NeedsRevocationIssue(HikiotPersonDevice item) => States(item).Any(x => x.InfoStatus == HikiotDeleteStatus || x.IsSending.GetValueOrDefault());
    private static bool NeedsIssue(HikiotPersonDevice item, bool revoking)
        => revoking ? NeedsRevocationIssue(item)
            : !IsFullyIssued(item);

    private static void ValidateEmployee(AccessPerson person)
    {
        if (person.Kind != PersonKind.Employee) throw new InvalidOperationException("Only employees use HIKIoT authority configuration issuance.");
        if (person.Status != PersonStatus.Active) throw new InvalidOperationException("Only active employees can receive HIKIoT permissions.");
        if (string.IsNullOrWhiteSpace(person.HikiotPersonNo)) throw new InvalidOperationException("Create or link the HIKIoT team member before issuing permissions.");
    }

    private static string BuildConfigName(AccessPerson person, AccessDevice device)
    {
        var serial = new string(device.DeviceSerial.Where(char.IsLetterOrDigit).ToArray());
        var raw = $"AIT-{person.EmployeeNo}-{serial}";
        return raw.Length <= 50 ? raw : raw[..50];
    }

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
