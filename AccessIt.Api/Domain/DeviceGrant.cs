namespace AccessIt.Api.Domain;

public class DeviceGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public Guid AccessDeviceId { get; set; }
    public AccessDevice AccessDevice { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AccessCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccessPersonId { get; set; }
    public AccessPerson AccessPerson { get; set; } = null!;
    public string CardNo { get; set; } = string.Empty;
    public bool IsVirtual { get; set; }
    // The team identification id is retained so an explicitly requested replacement can remove the right remote item.
    public long? HikiotIdentificationId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AccessDevice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DeviceSerial { get; set; } = string.Empty;
    public string? GroupNo { get; set; }
    public string? GroupName { get; set; }
    public bool IsManaged { get; set; } = true;
    public bool SupportsUserInfo { get; set; }
    public bool SupportsCardInfo { get; set; }
    public bool SupportsFace { get; set; }
    public bool SupportsPassword { get; set; }
    public bool SupportsPurePassword { get; set; }
    public bool SupportsRemoteOpen { get; set; }
    public bool SupportsUserRightPlanTemplate { get; set; }
    // The all-day plan/template used by direct user issuance has been initialized on this device.
    public bool HasAllDayTemplate { get; set; }
    public DateTime? LastSyncedAtUtc { get; set; }
    public ICollection<DeviceGrant> DeviceGrants { get; set; } = new List<DeviceGrant>();
}
