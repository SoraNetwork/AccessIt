using System.Globalization;
using System.Text.Json.Serialization;
using AccessIt.Api.Domain;

namespace AccessIt.Api.Hikiot;

public static class HikiotUserCommandFactory
{
    public static HikiotUserUpsertCommand Create(string deviceSerial, AccessPerson person, string? password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        if (!string.IsNullOrWhiteSpace(password) && (password.Length < 4 || password.Length > 6))
            throw new ArgumentOutOfRangeException(nameof(password), "HIKIoT passwords must contain 4 to 6 characters.");

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
                    DoorRight = [1],
                    DoorRightPlan =
                    [
                        new HikiotDoorRightPlan { DoorNo = 1, PlanTemplateId = [8] }
                    ],
                    Password = string.IsNullOrWhiteSpace(password) ? null : password,
                    MaxOpenDoorTime = person.Kind == PersonKind.Visitor ? person.MaxOpenDoorTime : null
                }
            }
        };
    }

    private static string FormatTime(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
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

    [JsonPropertyName("doorRightPlan")]
    public List<HikiotDoorRightPlan> DoorRightPlan { get; init; } = [];

    [JsonPropertyName("doorRight")]
    public List<int> DoorRight { get; init; } = [];

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
