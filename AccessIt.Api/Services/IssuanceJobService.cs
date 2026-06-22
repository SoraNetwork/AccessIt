using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;
using AccessIt.Api.Security;

namespace AccessIt.Api.Services;

public interface IIssuanceJobService
{
    Task QueueUpsertAsync(AccessPerson person, IEnumerable<AccessDevice> devices, string? actorUserId, CancellationToken cancellationToken = default);
    Task QueueDeleteAsync(AccessPerson person, IEnumerable<AccessDevice> devices, string? actorUserId, CancellationToken cancellationToken = default);
    Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default);
    Task RetryAsync(Guid jobId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class IssuanceJobService(
    AccessItDbContext db,
    IHikiotGateway hikiot,
    ISecretProtector secretProtector,
    IAuditService audit,
    TimeProvider timeProvider) : IIssuanceJobService
{
    public async Task QueueUpsertAsync(AccessPerson person, IEnumerable<AccessDevice> devices, string? actorUserId, CancellationToken cancellationToken = default)
    {
        var cards = person.Cards.ToList();
        var face = person.FaceAssets.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();
        var password = await db.DevicePasswords.SingleOrDefaultAsync(x => x.AccessPersonId == person.Id, cancellationToken);
        foreach (var device in devices.Where(x => x.IsManaged).DistinctBy(x => x.Id))
        {
            var workflowId = Guid.NewGuid();
            var steps = IssuanceWorkflowBuilder.BuildUpsertSteps(device, cards.Count > 0, face is not null, password is not null);
            var index = 0;
            foreach (var type in steps)
            {
                if (type == IssuanceStepType.UpsertCard)
                {
                    foreach (var card in cards)
                        AddJob(workflowId, ++index, person.Id, device.Id, type, card.Id);
                    continue;
                }
                AddJob(workflowId, ++index, person.Id, device.Id, type, type == IssuanceStepType.UpsertFace ? face?.Id : null);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "issuance.upsert.queued", "AccessPerson", person.Id, new { person.EmployeeNo }, cancellationToken);
    }

    public async Task QueueDeleteAsync(AccessPerson person, IEnumerable<AccessDevice> devices, string? actorUserId, CancellationToken cancellationToken = default)
    {
        var cards = person.Cards.ToList();
        var faces = person.FaceAssets.ToList();
        foreach (var device in devices.Where(x => x.IsManaged).DistinctBy(x => x.Id))
        {
            var workflowId = Guid.NewGuid();
            var index = 0;
            foreach (var face in faces) AddJob(workflowId, ++index, person.Id, device.Id, IssuanceStepType.DeleteFace, face.Id);
            foreach (var card in cards) AddJob(workflowId, ++index, person.Id, device.Id, IssuanceStepType.DeleteCard, card.Id);
            AddJob(workflowId, ++index, person.Id, device.Id, IssuanceStepType.DeleteUser, null);
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "issuance.delete.queued", "AccessPerson", person.Id, new { person.EmployeeNo }, cancellationToken);
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await db.IssuanceJobs
            .Where(x => x.Status == IssuanceJobStatus.Pending && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(50)
            .ToListAsync(cancellationToken);
        IssuanceJob? job = null;
        foreach (var candidate in candidates)
        {
            var predecessorStates = await db.IssuanceJobs
                .Where(x => x.ParentJobId == candidate.ParentJobId && x.Sequence < candidate.Sequence)
                .Select(x => x.Status)
                .ToListAsync(cancellationToken);

            if (predecessorStates.Any(x => x is IssuanceJobStatus.Failed or IssuanceJobStatus.Cancelled))
            {
                candidate.Status = IssuanceJobStatus.Cancelled;
                candidate.FailureMessage = "A preceding issuance step failed.";
                candidate.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(cancellationToken);
                await audit.WriteAsync(null, "issuance.cancelled", "IssuanceJob", candidate.Id, new { candidate.Type }, cancellationToken);
                return true;
            }

            if (predecessorStates.All(x => x == IssuanceJobStatus.Succeeded))
            {
                job = candidate;
                break;
            }
        }
        if (job is null) return false;

        job.Status = IssuanceJobStatus.Running;
        job.AttemptCount++;
        await db.SaveChangesAsync(cancellationToken);

        var result = await ExecuteAsync(job, cancellationToken);
        if (result.Succeeded)
        {
            job.Status = IssuanceJobStatus.Succeeded;
            job.TraceId = result.TraceId;
            job.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }
        else if (HikiotErrorClassifier.IsRetryable(result.Code) && job.AttemptCount < 4)
        {
            job.Status = IssuanceJobStatus.Pending;
            job.FailureCode = result.Code.ToString();
            job.FailureMessage = result.Message;
            job.NextAttemptAtUtc = timeProvider.GetUtcNow().AddMinutes(new[] { 1, 5, 15 }[job.AttemptCount - 1]).UtcDateTime;
        }
        else
        {
            job.Status = IssuanceJobStatus.Failed;
            job.FailureCode = result.Code.ToString();
            job.FailureMessage = string.IsNullOrWhiteSpace(result.Detail) ? result.Message : $"{result.Message}: {result.Detail}";
            job.CompletedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(null, result.Succeeded ? "issuance.succeeded" : "issuance.failed", "IssuanceJob", job.Id, new { job.Type, result.Code, result.Message, result.TraceId }, cancellationToken);
        return true;
    }

    public async Task RetryAsync(Guid jobId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var job = await db.IssuanceJobs.FindAsync([jobId], cancellationToken) ?? throw new KeyNotFoundException("Issuance job was not found.");
        if (job.Status == IssuanceJobStatus.Succeeded) throw new InvalidOperationException("Succeeded jobs cannot be retried.");
        job.Status = IssuanceJobStatus.Pending;
        job.NextAttemptAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        job.FailureCode = null;
        job.FailureMessage = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "issuance.retry", "IssuanceJob", job.Id, null, cancellationToken);
    }

    private void AddJob(Guid workflowId, int sequence, Guid personId, Guid deviceId, IssuanceStepType type, Guid? relatedEntityId)
    {
        db.IssuanceJobs.Add(new IssuanceJob
        {
            ParentJobId = workflowId,
            Sequence = sequence,
            AccessPersonId = personId,
            AccessDeviceId = deviceId,
            RelatedEntityId = relatedEntityId,
            Type = type
        });
    }

    private async Task<HikiotOperationResult> ExecuteAsync(IssuanceJob job, CancellationToken cancellationToken)
    {
        var person = job.AccessPersonId is Guid personId
            ? await db.AccessPeople.Include(x => x.Cards).Include(x => x.FaceAssets).SingleOrDefaultAsync(x => x.Id == personId, cancellationToken)
            : null;
        var device = job.AccessDeviceId is Guid deviceId ? await db.AccessDevices.FindAsync([deviceId], cancellationToken) : null;
        if (person is null || device is null) return HikiotOperationResult.Failure(160103, "The person or device no longer exists.");

        return job.Type switch
        {
            IssuanceStepType.EnsureAllDayTemplate => await hikiot.EnsureAllDayTemplateAsync(device.DeviceSerial, cancellationToken),
            IssuanceStepType.UpsertUser => await hikiot.UpsertUserAsync(device.DeviceSerial, person, await GetPasswordAsync(person.Id, cancellationToken), cancellationToken),
            IssuanceStepType.UpsertCard => await ExecuteCardAsync(device.DeviceSerial, person, job.RelatedEntityId, false, cancellationToken),
            IssuanceStepType.UpsertFace => await ExecuteFaceAsync(device.DeviceSerial, person, job.RelatedEntityId, false, cancellationToken),
            IssuanceStepType.DeleteCard => await ExecuteCardAsync(device.DeviceSerial, person, job.RelatedEntityId, true, cancellationToken),
            IssuanceStepType.DeleteFace => await ExecuteFaceAsync(device.DeviceSerial, person, job.RelatedEntityId, true, cancellationToken),
            IssuanceStepType.DeleteUser => await hikiot.DeleteUserAsync(device.DeviceSerial, person.EmployeeNo, cancellationToken),
            _ => HikiotOperationResult.Failure(160103, "Unsupported issuance step.")
        };
    }

    private async Task<string?> GetPasswordAsync(Guid personId, CancellationToken cancellationToken)
    {
        var password = await db.DevicePasswords.SingleOrDefaultAsync(x => x.AccessPersonId == personId, cancellationToken);
        return password is null ? null : secretProtector.Unprotect(password.ProtectedValue);
    }

    private async Task<HikiotOperationResult> ExecuteCardAsync(string deviceSerial, AccessPerson person, Guid? cardId, bool delete, CancellationToken cancellationToken)
    {
        if (cardId is not Guid id) return HikiotOperationResult.Failure(160103, "Card is missing from the issuance job.");
        var card = await db.AccessCards.FindAsync([id], cancellationToken);
        return card is null ? HikiotOperationResult.Failure(160103, "Card no longer exists.")
            : delete ? await hikiot.DeleteCardAsync(deviceSerial, card.CardNo, cancellationToken)
            : await hikiot.UpsertCardAsync(deviceSerial, person, card, cancellationToken);
    }

    private async Task<HikiotOperationResult> ExecuteFaceAsync(string deviceSerial, AccessPerson person, Guid? faceId, bool delete, CancellationToken cancellationToken)
    {
        if (faceId is not Guid id) return HikiotOperationResult.Failure(160103, "Face image is missing from the issuance job.");
        var face = await db.FaceAssets.FindAsync([id], cancellationToken);
        return face is null ? HikiotOperationResult.Failure(160103, "Face image no longer exists.")
            : delete ? await hikiot.DeleteFaceAsync(deviceSerial, person.EmployeeNo, cancellationToken)
            : await hikiot.UpsertFaceAsync(deviceSerial, person, face, cancellationToken);
    }
}
