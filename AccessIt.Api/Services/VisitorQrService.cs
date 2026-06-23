using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

public sealed record VisitorQrIssueResult(VisitorQrShare Share, string ShareUrl);

public interface IVisitorQrService
{
    Task<VisitorQrIssueResult> IssueAsync(Guid visitorId, Guid deviceId, int expireMinutes, int maxOpenTimes, string hostUserId, CancellationToken cancellationToken = default);
    Task<VisitorQrShare?> GetPublicAsync(string token, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid shareId, string actorUserId, CancellationToken cancellationToken = default);
    Task NotifyHostAsync(Guid shareId, string actorUserId, CancellationToken cancellationToken = default);
}

public sealed class VisitorQrService(
    AccessItDbContext db,
    IHikiotGateway hikiot,
    IDingTalkGateway dingTalk,
    IOptions<HikiotOptions> options,
    IAuditService audit,
    TimeProvider timeProvider) : IVisitorQrService
{
    private readonly HikiotOptions _options = options.Value;

    public async Task<VisitorQrIssueResult> IssueAsync(Guid visitorId, Guid deviceId, int expireMinutes, int maxOpenTimes, string hostUserId, CancellationToken cancellationToken = default)
    {
        var visitor = await db.AccessPeople.Include(x => x.Cards).SingleOrDefaultAsync(x => x.Id == visitorId, cancellationToken)
                      ?? throw new KeyNotFoundException("Visitor was not found.");
        if (visitor.Kind != PersonKind.Visitor) throw new InvalidOperationException("Only visitors can receive a visitor QR code.");
        var device = await db.AccessDevices.FindAsync([deviceId], cancellationToken) ?? throw new KeyNotFoundException("Device was not found.");
        var hasGrant = await db.DeviceGrants.AnyAsync(x => x.AccessPersonId == visitorId && x.AccessDeviceId == deviceId && x.IsActive, cancellationToken);
        if (!hasGrant) throw new InvalidOperationException("Visitor is not authorized for the selected device.");
        if (!device.SupportsUserInfo || !device.SupportsCardInfo)
            throw new InvalidOperationException("The selected device does not support visitor QR cards.");

        var now = timeProvider.GetUtcNow();
        var visitorEnd = new DateTimeOffset(visitor.EnableEndTime).ToUniversalTime();
        var maximumMinutes = (int)Math.Floor((visitorEnd - now).TotalMinutes);
        if (maximumMinutes < 5) throw new InvalidOperationException("Visitor access has expired or will expire in less than five minutes.");
        expireMinutes = Math.Clamp(expireMinutes, 5, Math.Min(10080, maximumMinutes));

        var card = visitor.Cards.FirstOrDefault(x => x.IsVirtual);
        if (card is null)
        {
            card = new AccessCard { AccessPersonId = visitor.Id, CardNo = await CreateVirtualCardNoAsync(visitor.Sequence, cancellationToken), IsVirtual = true };
            visitor.Cards.Add(card);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(hostUserId, "visitor.virtual-card.created", "AccessCard", card.Id, new { visitor.EmployeeNo, card.CardNo }, cancellationToken);
        }

        // QR generation requires the card to exist on the target device. Make the prerequisite explicit and synchronous,
        // rather than asking an operator to infer whether a background issuance job has already completed.
        if (device.SupportsUserRightPlanTemplate && !device.HasAllDayTemplate)
        {
            var template = await hikiot.EnsureAllDayTemplateAsync(device.DeviceSerial, cancellationToken);
            if (!template.Succeeded) throw new InvalidOperationException($"Unable to prepare the device access template: {template.Message}");
            device.HasAllDayTemplate = true;
            await db.SaveChangesAsync(cancellationToken);
        }
        var user = await hikiot.UpsertUserAsync(device.DeviceSerial, visitor, null, cancellationToken);
        // HasAllDayTemplate is a local cache. A device reset or a prior failed initialization can
        // make it stale; HIK reports that as the generic 160103 setting error. Rebuild our managed
        // all-day template once and retry the visitor user before reporting the real device error.
        if (!user.Succeeded && device.SupportsUserRightPlanTemplate && user.Code == 160103)
        {
            var template = await hikiot.EnsureAllDayTemplateAsync(device.DeviceSerial, cancellationToken);
            if (!template.Succeeded) throw new InvalidOperationException($"Unable to restore the device access template: {template.Code} {template.Message}{FormatDetail(template.Detail)}");
            device.HasAllDayTemplate = true;
            await db.SaveChangesAsync(cancellationToken);
            user = await hikiot.UpsertUserAsync(device.DeviceSerial, visitor, null, cancellationToken);
        }
        if (!user.Succeeded) throw new InvalidOperationException($"Unable to issue visitor to the device: {user.Code} {user.Message}{FormatDetail(user.Detail)}");
        var issuedCard = await hikiot.UpsertCardAsync(device.DeviceSerial, visitor, card, cancellationToken);
        if (!issuedCard.Succeeded) throw new InvalidOperationException($"Unable to issue the visitor QR card to the device: {issuedCard.Code} {issuedCard.Message}{FormatDetail(issuedCard.Detail)}");
        await WaitForVirtualCardAsync(device.DeviceSerial, visitor.EmployeeNo, cancellationToken);
        await audit.WriteAsync(hostUserId, "visitor.virtual-card.issued", "AccessCard", card.Id, new { visitor.EmployeeNo, device.DeviceSerial, issuedCard.TraceId }, cancellationToken);

        var result = await hikiot.GenerateVisitorQrAsync(device.DeviceSerial, card.CardNo, expireMinutes, maxOpenTimes, cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.QrCode) || result.ExpiresAtUtc is null)
            throw new InvalidOperationException($"二维码生成失败：{result.Message}");

        var share = new VisitorQrShare
        {
            AccessPersonId = visitorId,
            OpaqueToken = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            QrCodeContent = result.QrCode,
            ExpiresAtUtc = result.ExpiresAtUtc.Value,
            IssuedToHostUserId = hostUserId
        };
        db.VisitorQrShares.Add(share);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(hostUserId, "visitor.qr.issued", "VisitorQrShare", share.Id, new { visitor.EmployeeNo, device.DeviceSerial, result.TraceId }, cancellationToken);
        return new VisitorQrIssueResult(share, BuildShareUrl(share.OpaqueToken));
    }

    public async Task<VisitorQrShare?> GetPublicAsync(string token, CancellationToken cancellationToken = default)
    {
        var share = await db.VisitorQrShares.SingleOrDefaultAsync(x => x.OpaqueToken == token, cancellationToken);
        return share is null || share.RevokedAtUtc is not null || share.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime ? null : share;
    }

    public async Task RevokeAsync(Guid shareId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var share = await db.VisitorQrShares.FindAsync([shareId], cancellationToken) ?? throw new KeyNotFoundException("QR share was not found.");
        share.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(actorUserId, "visitor.qr.revoked", "VisitorQrShare", shareId, null, cancellationToken);
    }

    public async Task NotifyHostAsync(Guid shareId, string actorUserId, CancellationToken cancellationToken = default)
    {
        var share = await db.VisitorQrShares.FindAsync([shareId], cancellationToken) ?? throw new KeyNotFoundException("QR share was not found.");
        if (string.IsNullOrWhiteSpace(share.IssuedToHostUserId)) throw new InvalidOperationException("No host user is attached to this QR share.");
        await dingTalk.SendWorkNoticeAsync([share.IssuedToHostUserId], $"访客开门二维码：{BuildShareUrl(share.OpaqueToken)}\n有效期至：{share.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}", cancellationToken);
        await audit.WriteAsync(actorUserId, "visitor.qr.host-notified", "VisitorQrShare", shareId, null, cancellationToken);
    }

    private string BuildShareUrl(string token)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl)) throw new InvalidOperationException("Hikiot:PublicBaseUrl is not configured.");
        return $"{_options.PublicBaseUrl.TrimEnd('/')}/public/visitor-qr/{Uri.EscapeDataString(token)}";
    }

    private async Task<string> CreateVirtualCardNoAsync(long sequence, CancellationToken cancellationToken)
    {
        var prefix = $"Q{sequence:X10}";
        var candidate = prefix;
        for (var attempt = 0; await db.AccessCards.AnyAsync(x => x.CardNo == candidate, cancellationToken); attempt++)
        {
            if (attempt >= 8) throw new InvalidOperationException("Unable to allocate a unique virtual card number.");
            candidate = $"Q{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..12]}";
        }
        return candidate;
    }

    private async Task WaitForVirtualCardAsync(string deviceSerial, string employeeNo, CancellationToken cancellationToken)
    {
        // Device-direct calls acknowledge receipt with a traceId before the hardware has committed
        // the card. QR generation is stricter and refuses that short window, hence the former
        // intermittent “issue a virtual card first” failure.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var people = await hikiot.SearchPeopleAsync(deviceSerial, 1, 20, employeeNo, cancellationToken);
            var person = people.People.SingleOrDefault(x => x.EmployeeNo == employeeNo);
            if (people.Succeeded && person is not null && person.CardCount > 0) return;
            if (attempt < 7) await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
        }
        throw new InvalidOperationException("The device has not confirmed the visitor virtual card yet. Check the device online state and retry; a QR code was not generated.");
    }

    private static string FormatDetail(string? detail) => string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}";
}
