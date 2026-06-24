using AccessIt.Api.Services;

namespace AccessIt.Api.Domain;

/// <summary>系统角色。新开发时按需扩展。</summary>
public enum ApplicationRole
{
    None,
    SuperAdmin,
    AccessAdmin,
    Auditor
}

public enum AccessPersonKind { Employee, Visitor }
public enum PersonSourceType { Hikiot, DingTalk }

/// <summary>系统内的统一人员档案。访客不进入海康团队，仅保留设备直连编号。</summary>
public class AccessPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public AccessPersonKind Kind { get; set; } = AccessPersonKind.Employee;
    public string? HikiotPersonNo { get; set; }
    public string DeviceEmployeeNo { get; set; } = string.Empty;
    public string? CardNo { get; set; }
    public List<AccessCard> Cards { get; set; } = [];
    /// <summary>从海康团队同步的人脸地址；仅表示远端已有人脸，不等同于本地上传的人脸资产。</summary>
    public string? HikiotFaceUrl { get; set; }
    public Guid? FaceAssetId { get; set; }
    public FaceAsset? FaceAsset { get; set; }
    public bool PermanentValid { get; set; }
    public DateTime? EnableBeginTimeUtc { get; set; }
    public DateTime? EnableEndTimeUtc { get; set; }
    public string? QrShareToken { get; set; }
    public string? QrContent { get; set; }
    public DateTime? QrRevokedAtUtc { get; set; }
    public string? LastIssueResultJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PersonSource> Sources { get; set; } = [];
}

/// <summary>人员可拥有多张门禁卡；卡号在系统内唯一。</summary>
public class AccessCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public string CardNo { get; set; } = string.Empty;
    public bool IsVirtual { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>保存来源身份而不是把钉钉/海康字段硬编码为单值，允许同名合并。</summary>
public class PersonSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public PersonSourceType SourceType { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? UnionId { get; set; }
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 登录用户（钉钉身份）。保留：登录认证与用户管理依赖此模型。
/// </summary>
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

/// <summary>
/// HIKIoT 连接（单行 Id=1）。保留：AT/UT token 缓存机制的持久化载体。
/// 加密存储 App/User Access Token 与对应 Refresh Token 及过期时间。
/// </summary>
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

/// <summary>HIKIoT 第三方授权 state（一次性，10 分钟有效）。保留：OAuth 授权闭环。</summary>
public class HikiotAuthorizationState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string State { get; set; } = string.Empty;
    public string RequestedByUserId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>审计事件。保留：通用基础设施。</summary>
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
