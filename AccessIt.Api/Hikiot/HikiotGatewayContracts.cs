using System.Text.Json.Serialization;

namespace AccessIt.Api.Hikiot;

/// <summary>
/// HIKIoT 网关契约 —— 重构后只保留 App/User Token 缓存机制与 OAuth 授权所需的骨架类型。
/// 所有业务 API（团队人员、设备下发、权限配置、访客二维码等）的契约随业务逻辑一并清除，
/// 待重新开发时按需新增。
/// </summary>
public interface IHikiotGateway
{
    /// <summary>当前连接与授权状态（供设置页展示 / 鉴权前置检查）。</summary>
    Task<HikiotConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>生成第三方授权跳转地址并落库 state（10 分钟有效）。</summary>
    Task<string> BeginAuthorizationAsync(string requestedByUserId, CancellationToken cancellationToken = default);

    /// <summary>OAuth 回调：用 authCode 换取并持久化 UserAccessToken。</summary>
    Task CompleteAuthorizationAsync(string state, string authCode, CancellationToken cancellationToken = default);
    Task SetDefaultDepartmentAsync(string departmentNo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HikiotTeamPerson>> GetTeamPeopleAsync(CancellationToken cancellationToken = default);
    Task<string> CreateTeamPersonAsync(string name, string? mobile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotIdentification>> GetTeamIdentificationsAsync(string personNo, CancellationToken cancellationToken = default);
    Task AddTeamCardAsync(string personNo, string cardNo, CancellationToken cancellationToken = default);
    Task AddTeamFaceAsync(string personNo, string faceUrl, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotDoorDevice>> GetDoorDevicesAsync(CancellationToken cancellationToken = default);
    Task<DeviceIssueResult> UpsertDirectUserAsync(HikiotDoorDevice device, HikiotDirectUser user, CancellationToken cancellationToken = default);
    Task<DeviceIssueResult> UpsertDirectCardAsync(HikiotDoorDevice device, string employeeNo, string cardNo, CancellationToken cancellationToken = default);
    Task<DeviceIssueResult> UpsertDirectFaceAsync(HikiotDoorDevice device, string employeeNo, string faceUrl, CancellationToken cancellationToken = default);
    Task<string?> GenerateVisitorQrAsync(HikiotDoorDevice device, string employeeNo, string cardNo, CancellationToken cancellationToken = default);
}

public sealed record HikiotTeamPerson(string PersonNo, string Name, string? Mobile);
public sealed record HikiotIdentification(int Type, string Value);
public sealed record HikiotDoorDevice(string Serial, string Name, bool SupportsUserInfo, bool SupportsCard, bool SupportsFace, bool SupportsPassword);
public sealed record HikiotDirectUser(string EmployeeNo, string Name, bool IsVisitor, bool PermanentValid, DateTime BeginUtc, DateTime EndUtc, string? Password);
public sealed record DeviceIssueResult(string DeviceSerial, bool Succeeded, string Message);

/// <summary>对外暴露的连接状态快照。</summary>
public sealed record HikiotConnectionStatus(
    bool IsAuthorized,
    bool NeedsReauthorization,
    string? TeamNo,
    string? DefaultDepartmentNo,
    DateTime? UserTokenExpiresAtUtc,
    string? LastError);

/// <summary>HIKIoT 统一响应信封。code == 0 视为成功。</summary>
public sealed class HikiotEnvelope<T>
{
    [JsonPropertyName("code")]
    public int Code { get; init; }
    [JsonPropertyName("msg")]
    public string Message { get; init; } = string.Empty;
    [JsonPropertyName("data")]
    public T? Data { get; init; }
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
    [JsonPropertyName("count")]
    public long Count { get; init; }
}

/// <summary>
/// HIKIoT 各 token 接口（exchangeAppToken / refreshAppToken / code2Token / refreshUserAccessToken）
/// 返回的数据结构。字段为各接口的并集，按需读取。
/// </summary>
public sealed class HikiotTokenData
{
    public string AppKey { get; init; } = string.Empty;
    public string? AppAccessToken { get; init; }
    public string? RefreshAppToken { get; init; }
    public string? UserAccessToken { get; init; }
    public string? RefreshUserToken { get; init; }
    public int ExpiresIn { get; init; }
    public string? TeamNo { get; init; }
    public string? AccountNo { get; init; }
}
