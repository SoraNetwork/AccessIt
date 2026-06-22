using Microsoft.EntityFrameworkCore;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

public interface IDeviceService
{
    Task<IReadOnlyList<AccessDevice>> DiscoverAsync(string actorUserId, CancellationToken cancellationToken = default);
    Task<HikiotOperationResult> OpenDoorAsync(Guid deviceId, string reason, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class DeviceService(AccessItDbContext db, IHikiotGateway hikiot, IAuditService audit) : IDeviceService
{
    public async Task<IReadOnlyList<AccessDevice>> DiscoverAsync(string actorUserId, CancellationToken cancellationToken = default)
    {
        var remoteDevices = await hikiot.DiscoverDevicesAsync(cancellationToken);
        var existing = await db.AccessDevices.ToDictionaryAsync(x => x.DeviceSerial, StringComparer.OrdinalIgnoreCase, cancellationToken);
        foreach (var remote in remoteDevices)
        {
            if (!existing.TryGetValue(remote.DeviceSerial, out var device))
            {
                device = new AccessDevice { DeviceSerial = remote.DeviceSerial };
                db.AccessDevices.Add(device);
                existing[remote.DeviceSerial] = device;
            }
            device.GroupNo = remote.GroupNo;
            device.GroupName = remote.GroupName;
            device.SupportsUserInfo = remote.Capacity.SupportUserInfo;
            device.SupportsCardInfo = remote.Capacity.SupportCardInfo;
            device.SupportsFace = remote.Capacity.SupportFace;
            device.SupportsPassword = remote.Capacity.SupportPassword;
            device.SupportsPurePassword = remote.Capacity.SupportPurePassword;
            device.SupportsRemoteOpen = remote.Capacity.SupportRemoteOpen;
            device.SupportsUserRightPlanTemplate = remote.Capacity.SupportUserRightPlanTemplate;
            device.LastSyncedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "device.discovery", "AccessDevice", "all", new { count = remoteDevices.Count }, cancellationToken);
        return existing.Values.OrderBy(x => x.GroupName).ThenBy(x => x.DeviceSerial).ToList();
    }

    public async Task<HikiotOperationResult> OpenDoorAsync(Guid deviceId, string reason, string actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var device = await db.AccessDevices.FindAsync([deviceId], cancellationToken) ?? throw new KeyNotFoundException("Device was not found.");
        if (!device.SupportsRemoteOpen) throw new InvalidOperationException("该设备不支持远程开门。");
        var result = await hikiot.OpenDoorAsync(device.DeviceSerial, cancellationToken);
        await audit.WriteAsync(actorUserId, "device.remote-open", "AccessDevice", device.Id, new { device.DeviceSerial, reason, result.Code, result.Message }, cancellationToken);
        return result;
    }
}
