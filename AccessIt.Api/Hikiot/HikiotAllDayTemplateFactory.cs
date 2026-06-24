using System.Text.Json.Serialization;

namespace AccessIt.Api.Hikiot;

public static class HikiotAllDayTemplateFactory
{
    public static HikiotAllDayTemplateCommands Create(string deviceSerial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);

        var segments = Enum.GetNames<HikiotWeek>()
            .Select((week, index) => new HikiotTimeSegment
            {
                DoorWeekTimeSegmentId = index + 1,
                Week = week,
                Enable = true,
                DoorStatus = "normal",
                BeginTime = "00:00:00",
                EndTime = "23:59:59"
            })
            .ToList();

        return new HikiotAllDayTemplateCommands(
            new HikiotWeekPlanCommand
            {
                DeviceSerial = deviceSerial,
                Payload = new HikiotWeekPlanPayload
                {
                    UserWeekPlan = new HikiotWeekPlan { DoorWeekId = 8, Enable = true, TimeSegment = segments }
                }
            },
            new HikiotUserPlanTemplateCommand
            {
                DeviceSerial = deviceSerial,
                Payload = new HikiotUserPlanTemplatePayload
                {
                    UserPlanTemplate = new HikiotUserPlanTemplate
                    {
                        DoorPlanTemplateId = 8,
                        DoorWeekId = 8,
                        Enable = true,
                        Name = "开一个门全天通行"
                    }
                }
            });
    }
}

public sealed record HikiotAllDayTemplateCommands(HikiotWeekPlanCommand WeekPlan, HikiotUserPlanTemplateCommand UserTemplate);

public enum HikiotWeek { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

public sealed class HikiotWeekPlanCommand
{
    [JsonPropertyName("deviceSerial")]
    public string DeviceSerial { get; init; } = string.Empty;
    [JsonPropertyName("payload")]
    public HikiotWeekPlanPayload Payload { get; init; } = new();
}

public sealed class HikiotWeekPlanPayload
{
    [JsonPropertyName("userWeekPlan")]
    public HikiotWeekPlan UserWeekPlan { get; init; } = new();
}

public sealed class HikiotWeekPlan
{
    [JsonPropertyName("doorWeekId")]
    public int DoorWeekId { get; init; }
    [JsonPropertyName("enable")]
    public bool Enable { get; init; }
    [JsonPropertyName("timeSegment")]
    public List<HikiotTimeSegment> TimeSegment { get; init; } = [];
}

public sealed class HikiotTimeSegment
{
    [JsonPropertyName("doorWeekTimeSegmentId")]
    public int DoorWeekTimeSegmentId { get; init; }
    [JsonPropertyName("enable")]
    public bool Enable { get; init; }
    [JsonPropertyName("week")]
    public string Week { get; init; } = string.Empty;
    [JsonPropertyName("doorStatus")]
    public string DoorStatus { get; init; } = string.Empty;
    [JsonPropertyName("beginTime")]
    public string BeginTime { get; init; } = string.Empty;
    [JsonPropertyName("endTime")]
    public string EndTime { get; init; } = string.Empty;
}

public sealed class HikiotUserPlanTemplateCommand
{
    [JsonPropertyName("deviceSerial")]
    public string DeviceSerial { get; init; } = string.Empty;
    [JsonPropertyName("payload")]
    public HikiotUserPlanTemplatePayload Payload { get; init; } = new();
}

public sealed class HikiotUserPlanTemplatePayload
{
    [JsonPropertyName("userPlanTemplate")]
    public HikiotUserPlanTemplate UserPlanTemplate { get; init; } = new();
}

public sealed class HikiotUserPlanTemplate
{
    [JsonPropertyName("doorPlanTemplateId")]
    public int DoorPlanTemplateId { get; init; }
    [JsonPropertyName("enable")]
    public bool Enable { get; init; }
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
    [JsonPropertyName("doorWeekId")]
    public int DoorWeekId { get; init; }
    [JsonPropertyName("holidayGroupNo")]
    public List<int> HolidayGroupNo { get; init; } = [];
}
