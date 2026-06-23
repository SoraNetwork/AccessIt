using System.Globalization;
using System.Text.Json.Serialization;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Hikiot;

public static class HikiotUserCommandFactory
{
    /// <summary>
    /// Builds an addOneRecord command for the given device and person.
    /// </summary>
    /// <param name="deviceSerial">Target device serial number.</param>
    /// <param name="person">The person whose credentials are being written.</param>
    /// <param name="password">
    ///   Optional password. Only sent for devices where <see cref="AccessDevice.SupportsPurePassword"/> is true.
    ///   Must be within the device-declared length bounds (typically 4–8 digits).
    /// </param>
    /// <param name="userRightPlanTemplateId">
    ///   Optional time-plan template ID returned by <c>EnsureAllDayTemplateAsync</c>.
    ///   When null the command omits <c>doorRightPlan</c> entirely so HIKIoT uses the device default.
    ///   Never pass a hardcoded value — use the template ID that was persisted on <see cref="AccessDevice"/>.
    /// </param>
    public static HikiotUserUpsertCommand Create(
        string deviceSerial,
        AccessPerson person,
        string? password,
        int? userRightPlanTemplateId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);

        // HIKIoT documents password length as 4–8 digits; devices may declare a narrower range.
        // We validate at the service layer using the device-reported capability. Here we only
        // guard against obviously invalid values to prevent silent truncation on the device side.
        if (!string.IsNullOrWhiteSpace(password) && (password.Length < 4 || password.Length > 8))
            throw new ArgumentOutOfRangeException(nameof(password), "Device passwords must be 4 to 8 digits.");

        List<HikiotDoorRightPlan>? doorRightPlan = null;
        if (userRightPlanTemplateId.HasValue)
        {
            doorRightPlan =
            [
                new HikiotDoorRightPlan { DoorNo = 1, PlanTemplateId = [userRightPlanTemplateId.Value] }
            ];
        }

        return new HikiotUserUpsertCommand
        {
            DeviceSerial = deviceSerial,
            Payload = new HikiotUserPayload
            {
                UserInfo = new HikiotUserInfo
                {
                    EmployeeNo = person.EmployeeNo,
                    Name = person.Name,
                    UserType = person.Kind == PersonKind.Employee ? "normal" : "visitor",
                    PermanentValid = person.PermanentValid,
                    EnableBeginTime = FormatTime(person.EnableBeginTime),
                    EnableEndTime = FormatTime(person.EnableEndTime),
                    // doorRight=1 means "Door 1". Only include it when we also send a doorRightPlan,
                    // so we do not accidentally overwrite HIKIoT-admin-managed door permissions
                    // on devices that are not using our plan template.
                    DoorRight = doorRightPlan is not null ? [1] : [],
                    DoorRightPlan = doorRightPlan,
                    Password = string.IsNullOrWhiteSpace(password) ? null : password,
                    MaxOpenDoorTime = person.Kind == PersonKind.Visitor ? person.MaxOpenDoorTime : null
                }
            }
        };
    }

    private static string FormatTime(DateTime value)
        => value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
}

public sealed class HikiotUserUpsertCommand
{
    [JsonPropertyName("deviceSerial")]
    public string DeviceSerial { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public HikiotUserPayload Payload { get; init; } = new();
}

public sealed class HikiotUserPayload
{
    [JsonPropertyName("userInfo")]
    public HikiotUserInfo UserInfo { get; init; } = new();
}

public sealed class HikiotUserInfo
{
    [JsonPropertyName("employeeNo")]
    public string EmployeeNo { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("userType")]
    public string UserType { get; init; } = string.Empty;

    [JsonPropertyName("permanentValid")]
    public bool PermanentValid { get; init; }

    [JsonPropertyName("enableBeginTime")]
    public string EnableBeginTime { get; init; } = string.Empty;

    [JsonPropertyName("enableEndTime")]
    public string EnableEndTime { get; init; } = string.Empty;

    /// <summary>Omit from payload when empty so we don't clear HIKIoT-admin-managed door rights.</summary>
    [JsonPropertyName("doorRight")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<int> DoorRight { get; init; } = [];

    [JsonPropertyName("doorRightPlan")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HikiotDoorRightPlan>? DoorRightPlan { get; init; }

    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    [JsonPropertyName("maxOpenDoorTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOpenDoorTime { get; init; }
}

public sealed class HikiotDoorRightPlan
{
    [JsonPropertyName("doorNo")]
    public int DoorNo { get; init; }

    [JsonPropertyName("planTemplateId")]
    public List<int> PlanTemplateId { get; init; } = [];
}
