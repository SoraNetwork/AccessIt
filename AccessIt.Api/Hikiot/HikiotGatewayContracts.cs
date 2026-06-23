using System.Text.Json;
using System.Text.Json.Serialization;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Hikiot;

public interface IHikiotGateway
{
    Task<HikiotConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotTeamDepartment>> GetTeamDepartmentsAsync(CancellationToken cancellationToken = default);
    Task SetDefaultDepartmentAsync(string departmentNo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotTeamPerson>> GetTeamPeopleAsync(CancellationToken cancellationToken = default);
    Task<HikiotTeamPerson?> GetTeamPersonAsync(string personNo, CancellationToken cancellationToken = default);
    Task<HikiotTeamPersonCreateResult> CreateTeamPersonAsync(HikiotTeamPersonUpsert request, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> UpdateTeamPersonAsync(HikiotTeamPersonUpsert request, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> RemoveTeamPersonAsync(string personNo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotIdentification>> GetTeamIdentificationsAsync(string personNo, CancellationToken cancellationToken = default);
    Task<HikiotIdentificationResult> AddTeamIdentificationAsync(string personNo, HikiotIdentificationType type, string content, CancellationToken cancellationToken = default);
    Task<HikiotIdentificationResult> AddTeamFaceAsync(string personNo, FaceAsset face, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> DeleteTeamIdentificationAsync(long identificationId, CancellationToken cancellationToken = default);
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
    Task<HikiotAuthorityConfigResult> SaveAuthorityConfigAsync(HikiotAuthorityConfigRequest request, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> DeleteAuthorityConfigAsync(string configId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotPersonDevice>> GetPersonDevicesAsync(string personNo, IReadOnlyCollection<string>? deviceSerials, int page, int size, CancellationToken cancellationToken = default);
    Task<HikiotIssueBatchResult> SelectIssueAsync(IReadOnlyCollection<long> personDeviceIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HikiotIssueBatchDetail>> GetIssueBatchDetailsAsync(string batchNo, int page, int size, CancellationToken cancellationToken = default);
}

public sealed record HikiotConnectionStatus(bool IsAuthorized, bool NeedsReauthorization, string? TeamNo, string? DefaultDepartmentNo, DateTime? UserTokenExpiresAtUtc, string? LastError);
public sealed record HikiotDiscoveredDevice(string GroupNo, string GroupName, string DeviceSerial, HikiotDeviceCapacity Capacity);
public sealed record HikiotDeviceCapacity(bool SupportUserInfo, bool SupportCardInfo, bool SupportFace, bool SupportPassword, bool SupportPurePassword, bool SupportRemoteOpen, bool SupportUserRightPlanTemplate);
public sealed record HikiotOperationResult(bool Succeeded, int Code, string Message, string? TraceId = null, string? Detail = null)
{
    public static HikiotOperationResult Failure(int code, string message, string? detail = null) => new(false, code, message, null, detail);
}
public sealed record HikiotQrCodeResult(bool Succeeded, int Code, string Message, string? TraceId, string? QrCode, DateTime? ExpiresAtUtc, string? Detail = null);
public sealed record HikiotRemotePerson(string EmployeeNo, string Name, string UserType, bool PermanentValid, DateTime? BeginTime, DateTime? EndTime, int OpenDoorTime, int MaxOpenDoorTime, int CardCount, int FaceCount);
public sealed record HikiotPeopleSearchResult(bool Succeeded, int Code, string Message, long Count, IReadOnlyList<HikiotRemotePerson> People, string? Detail = null);
public sealed record HikiotTeamDepartment(string DepartmentNo, string Name, string? ParentDepartmentNo, bool IsLeaf, bool HasPeople, int PersonCount, string? Path);
public sealed record HikiotTeamPerson(string PersonNo, string Name, string? Phone, string? TeamNo, string? DepartmentNo, string? JobNumber, string? JobPosition, int Sex, bool IsOwner, string? PathName, string? IdCard);
public sealed record HikiotTeamPersonUpsert(string? PersonNo, string Name, string DepartmentNo, string Phone, string? JobNumber, string? JobPosition, int? Sex, string? IdCard = null);
public sealed record HikiotTeamPersonCreateResult(bool Succeeded, int Code, string Message, string? PersonNo, string? Detail = null);
public enum HikiotIdentificationType { Card = 1, FaceUrl = 3 }
public sealed record HikiotIdentification(long Id, HikiotIdentificationType Type, string Content);
public sealed record HikiotIdentificationResult(bool Succeeded, int Code, string Message, long? IdentificationId, string? Detail = null);
public sealed record HikiotAuthorityConfigRequest(string? ConfigId, string ConfigName, string ConfigDescription, string PersonNo, string DeviceSerial, int? TimePlanId = null);
public sealed record HikiotAuthorityConfigResult(bool Succeeded, int Code, string Message, string? ConfigId, string? Detail = null);
public sealed record HikiotCredentialIssueState(string Credential, int? InfoStatus, bool? IsSupported, bool? IsSending, string? LastFailedReason);
public sealed record HikiotPersonDevice(long Id, string PersonNo, string DeviceSerial, int? InfoStatus, bool? IsSupported, bool? IsSending, string? LastFailedReason, IReadOnlyList<HikiotCredentialIssueState> CredentialStates);
public sealed record HikiotIssueBatchResult(bool Succeeded, int Code, string Message, string? BatchNo, string? Detail = null);
public sealed record HikiotIssueBatchDetail(long? PersonDeviceId, string? Status, bool? Succeeded, string? FailureReason);

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
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportUserInfo { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportCardInfo { get; init; }
    [JsonPropertyName("supportFDLib")]
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportFace { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportUserPassword { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportPurePwdVerify { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportRemoteControlDoor { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool SupportUserRightPlanTemplate { get; init; }
}

public sealed class HikiotTeamDepartmentData
{
    public List<HikiotTeamDepartmentPayload> TeamDepartVOs { get; init; } = [];
}

public sealed class HikiotTeamDepartmentPayload
{
    public string DepartNo { get; init; } = string.Empty;
    public string DepartName { get; init; } = string.Empty;
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string? ParentId { get; init; }
    public string? Path { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool IsLeaf { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool HavePerson { get; init; }
    public int PersonNum { get; init; }
}

public sealed class HikiotTeamPersonPayload
{
    public string PersonNo { get; init; } = string.Empty;
    public string PersonName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? TeamNo { get; init; }
    public string? DepartNo { get; init; }
    public string? JobNumber { get; init; }
    public string? JobPosition { get; init; }
    public int Sex { get; init; }
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool IsOwner { get; init; }
    public string? PathName { get; init; }
    public string? IdCard { get; init; }
}

public sealed class HikiotTeamPersonCreatedData
{
    public string? PersonNo { get; init; }
}

public sealed class HikiotIdentificationPayload
{
    public long Id { get; init; }
    public int Type { get; init; }
    public string Content { get; init; } = string.Empty;
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
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool PermanentValid { get; init; }
    public HikiotValidity? Valid { get; init; }
    public int OpenDoorTime { get; init; }
    public int MaxOpenDoorTime { get; init; }
    public int NumOfCard { get; init; }
    public int NumOfFace { get; init; }
}

public sealed class HikiotValidity
{
    [JsonConverter(typeof(NumberOrBooleanConverter))]
    public bool Enable { get; init; }
    public string? BeginTime { get; init; }
    public string? EndTime { get; init; }
}

public sealed class NumberOrBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Number when reader.TryGetInt64(out var value) => value != 0,
            JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
            JsonTokenType.String when long.TryParse(reader.GetString(), out var value) => value != 0,
            _ => throw new JsonException("Expected a boolean or 0/1 value.")
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

/// <summary>HIKIoT returns root department parentId as numeric 0, but child values as strings.</summary>
public sealed class StringOrNumberConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var number) => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            _ => throw new JsonException("Expected a string, number, boolean, or null value.")
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}
