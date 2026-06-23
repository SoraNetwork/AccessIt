namespace AccessIt.Api.Domain;

public enum ApplicationRole
{
    None,
    SuperAdmin,
    AccessAdmin,
    Auditor
}

public enum IssuanceJobStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public enum IssuanceStepType
{
    EnsureAllDayTemplate,
    UpsertUser,
    UpsertCard,
    UpsertFace,
    DeleteFace,
    DeleteCard,
    DeleteUser
}

public enum SyncRunStatus
{
    Running,
    Completed,
    Failed
}

public enum SyncConflictResolution
{
    Pending,
    KeepLocal,
    KeepDevice
}

public class ApplicationUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DingTalkUserId { get; set; } = string.Empty;
    public string? DingTalkUnionId { get; set; }
    public string? DingTalkOpenId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public ApplicationRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
    public DateTime? LastDirectorySyncAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class HikiotConnection
{
    public int Id { get; set; } = 1;
    public string? TeamNo { get; set; }
    public string? DefaultDepartmentNo { get; set; }
    public string? AccountNo { get; set; }
    public string? AuthorizedByUserId { get; set; }
    public string? ProtectedAppAccessToken { get; set; }
    public string? ProtectedRefreshAppToken { get; set; }
    public DateTime? AppTokenExpiresAtUtc { get; set; }
    public string? ProtectedUserAccessToken { get; set; }
    public string? ProtectedRefreshUserToken { get; set; }
    public DateTime? UserTokenExpiresAtUtc { get; set; }
    public DateTime? AuthorizedAtUtc { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }
    public string? LastError { get; set; }
    public bool NeedsReauthorization { get; set; } = true;
}

public class HikiotAuthorizationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public class FaceAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public string StoragePath { get; set; } = string.Empty;
    public string PublicToken { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public long ByteLength { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    // HIKIoT never returns a downloadable face image. This only records the remote team's identification id.
    public long? HikiotIdentificationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DevicePassword
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public string ProtectedValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class IssuanceJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? AccessPersonId { get; set; }
    public Guid? AccessDeviceId { get; set; }
    public Guid? ParentJobId { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public int Sequence { get; set; }
    public IssuanceStepType Type { get; set; }
    public IssuanceJobStatus Status { get; set; } = IssuanceJobStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public string? TraceId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public class SyncRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessDeviceId { get; set; }
    public string StartedByUserId { get; set; } = string.Empty;
    public SyncRunStatus Status { get; set; } = SyncRunStatus.Running;
    public int RemoteCount { get; set; }
    public int NewCount { get; set; }
    public int ConflictCount { get; set; }
    public string? FailureMessage { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}

public class SyncConflict
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SyncRunId { get; set; }
    public Guid? AccessPersonId { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string? LocalValue { get; set; }
    public string? RemoteValue { get; set; }
    public SyncConflictResolution Resolution { get; set; } = SyncConflictResolution.Pending;
    public string? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public class VisitorQrShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public string OpaqueToken { get; set; } = string.Empty;
    public string QrCodeContent { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? IssuedToHostUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public class PersonNumberSequence
{
    public PersonKind Kind { get; set; }
    public long LastValue { get; set; }
}
