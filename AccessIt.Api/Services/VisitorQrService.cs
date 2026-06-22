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
        var card = visitor.Cards.FirstOrDefault(x => x.IsVirtual) ?? throw new InvalidOperationException("Create and successfully issue a virtual card before generating the QR code.");

        var now = timeProvider.GetUtcNow();
        var visitorEnd = new DateTimeOffset(visitor.EnableEndTime).ToUniversalTime();
        var maximumMinutes = (int)Math.Floor((visitorEnd - now).TotalMinutes);
        if (maximumMinutes < 5) throw new InvalidOperationException("Visitor access has expired or will expire in less than five minutes.");
        expireMinutes = Math.Clamp(expireMinutes, 5, Math.Min(10080, maximumMinutes));
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
}
