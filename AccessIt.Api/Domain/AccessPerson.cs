namespace AccessIt.Api.Domain;

public enum PersonStatus
{
    Active,
    Expired,
    Disabled,
    Deleted
}

public class AccessPerson
{
    public static readonly DateTime HikiotMinDate = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);
    public static readonly DateTime HikiotMaxDate = new(2037, 12, 31, 23, 59, 59, DateTimeKind.Local);

    public Guid Id { get; set; } = Guid.NewGuid();
    public long Sequence { get; set; }
    public string EmployeeNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public PersonKind Kind { get; set; }
    public PersonStatus Status { get; set; } = PersonStatus.Active;
    public string? DingTalkUserId { get; set; }
    public string? Mobile { get; set; }
    public bool PermanentValid { get; set; }
    public DateTime EnableBeginTime { get; set; }
    public DateTime EnableEndTime { get; set; }
    public int MaxOpenDoorTime { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<DeviceGrant> DeviceGrants { get; set; } = new List<DeviceGrant>();
    public ICollection<AccessCard> Cards { get; set; } = new List<AccessCard>();
    public ICollection<FaceAsset> FaceAssets { get; set; } = new List<FaceAsset>();

    public static AccessPerson CreateEmployee(long sequence, string name, string? dingTalkUserId)
    {
        ValidateName(name);
        return new AccessPerson
        {
            Sequence = sequence,
            EmployeeNo = PersonNumberGenerator.Create(PersonKind.Employee, sequence),
            Name = name.Trim(),
            Kind = PersonKind.Employee,
            PermanentValid = true,
            EnableBeginTime = HikiotMinDate,
            EnableEndTime = HikiotMaxDate,
            DingTalkUserId = string.IsNullOrWhiteSpace(dingTalkUserId) ? null : dingTalkUserId.Trim()
        };
    }

    public static AccessPerson CreateVisitor(long sequence, string name, DateTime begin, DateTime end, int maxOpenDoorTime)
    {
        ValidateName(name);
        ValidateWindow(begin, end);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOpenDoorTime, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxOpenDoorTime, 255);

        return new AccessPerson
        {
            Sequence = sequence,
            EmployeeNo = PersonNumberGenerator.Create(PersonKind.Visitor, sequence),
            Name = name.Trim(),
            Kind = PersonKind.Visitor,
            PermanentValid = false,
            EnableBeginTime = begin,
            EnableEndTime = end,
            MaxOpenDoorTime = maxOpenDoorTime
        };
    }

    public void UpdateVisitorWindow(DateTime begin, DateTime end, int maxOpenDoorTime)
    {
        if (Kind != PersonKind.Visitor)
            throw new InvalidOperationException("Only visitors have a configurable validity window.");

        ValidateWindow(begin, end);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxOpenDoorTime, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxOpenDoorTime, 255);
        EnableBeginTime = begin;
        EnableEndTime = end;
        MaxOpenDoorTime = maxOpenDoorTime;
        Status = PersonStatus.Active;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (System.Text.Encoding.UTF8.GetByteCount(name.Trim()) > 32)
            throw new ArgumentOutOfRangeException(nameof(name), "Name must not exceed 32 UTF-8 bytes.");
    }

    private static void ValidateWindow(DateTime begin, DateTime end)
    {
        if (begin < HikiotMinDate || end > HikiotMaxDate || end <= begin)
            throw new ArgumentOutOfRangeException(nameof(end), "Visitor validity must be within HIKIoT's supported date range.");
    }
}
