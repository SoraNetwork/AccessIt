using System.Text.Json.Serialization;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Hikiot;

public interface IHikiotGateway
{
    Task<HikiotConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);
    Task<string> BeginAuthorizationAsync(string requestedByUserId, CancellationToken cancellationToken = default);
    Task CompleteAuthorizationAsync(string state, string authCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotDiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> EnsureAllDayTemplateAsync(string deviceSerial, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> UpsertUserAsync(string deviceSerial, AccessPerson person, string? password, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> UpsertCardAsync(string deviceSerial, AccessPerson person, AccessCard card, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> UpsertFaceAsync(string deviceSerial, AccessPerson person, FaceAsset face, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> DeleteUserAsync(string deviceSerial, string employeeNo, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> DeleteCardAsync(string deviceSerial, string cardNo, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> DeleteFaceAsync(string deviceSerial, string employeeNo, CancellationToken cancellationToken = default);
    Task<HikiotQrCodeResult> GenerateVisitorQrAsync(string deviceSerial, string cardNo, int expireMinutes, int maxOpenTimes, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> OpenDoorAsync(string resourceSerial, CancellationToken cancellationToken = default);
    Task<HikiotPeopleSearchResult> SearchPeopleAsync(string deviceSerial, int page, int size, string? keyword, CancellationToken cancellationToken = default);
}

public sealed record HikiotConnectionStatus(bool IsAuthorized, bool NeedsReauthorization, string? TeamNo, DateTime? UserTokenExpiresAtUtc, string? LastError);
public sealed record HikiotDiscoveredDevice(string GroupNo, string GroupName, string DeviceSerial, HikiotDeviceCapacity Capacity);
public sealed record HikiotDeviceCapacity(bool SupportUserInfo, bool SupportCardInfo, bool SupportFace, bool SupportPassword, bool SupportPurePassword, bool SupportRemoteOpen, bool SupportUserRightPlanTemplate);
public sealed record HikiotOperationResult(bool Succeeded, int Code, string Message, string? TraceId = null, string? Detail = null)
{
    public static HikiotOperationResult Failure(int code, string message, string? detail = null) => new(false, code, message, null, detail);
}
public sealed record HikiotQrCodeResult(bool Succeeded, int Code, string Message, string? TraceId, string? QrCode, DateTime? ExpiresAtUtc, string? Detail = null);
public sealed record HikiotRemotePerson(string EmployeeNo, string Name, string UserType, bool PermanentValid, DateTime? BeginTime, DateTime? EndTime, int OpenDoorTime, int MaxOpenDoorTime, int CardCount, int FaceCount);
public sealed record HikiotPeopleSearchResult(bool Succeeded, int Code, string Message, long Count, IReadOnlyList<HikiotRemotePerson> People, string? Detail = null);

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

public sealed class HikiotGroupPageItem
{
    public string DeviceGroupNo { get; init; } = string.Empty;
    public string DeviceGroupName { get; init; } = string.Empty;
    public List<string> DeviceSerialList { get; init; } = [];
}

public sealed class HikiotCapacityPayload
{
    public bool SupportUserInfo { get; init; }
    public bool SupportCardInfo { get; init; }
    [JsonPropertyName("supportFDLib")]
    public bool SupportFace { get; init; }
    public bool SupportUserPassword { get; init; }
    public bool SupportPurePwdVerify { get; init; }
    public bool SupportRemoteControlDoor { get; init; }
    public bool SupportUserRightPlanTemplate { get; init; }
}

public class HikiotTraceData
{
    public string? TraceId { get; init; }
}

public sealed class HikiotQrData
{
    public string? TraceId { get; init; }
    public string? QrCode { get; init; }
    public long? ExpireTime { get; init; }
}

public sealed class HikiotUserSearchItem
{
    public string EmployeeNo { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string UserType { get; init; } = string.Empty;
    public bool PermanentValid { get; init; }
    public HikiotValidity? Valid { get; init; }
    public int OpenDoorTime { get; init; }
    public int MaxOpenDoorTime { get; init; }
    public int NumOfCard { get; init; }
    public int NumOfFace { get; init; }
}

public sealed class HikiotValidity
{
    public bool Enable { get; init; }
    public string? BeginTime { get; init; }
    public string? EndTime { get; init; }
}
