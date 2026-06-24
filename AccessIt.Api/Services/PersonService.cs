using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.DingTalk;
using AccessIt.Api.Domain;
using AccessIt.Api.Hikiot;

namespace AccessIt.Api.Services;

public sealed record SyncResult(int Created, int Updated);
public sealed record PersonIssueResult(string DeviceSerial, bool Succeeded, string Message);

public interface IPersonService
{
    Task<SyncResult> SyncHikiotAsync(CancellationToken cancellationToken = default);
    Task<SyncResult> SyncDingTalkAsync(IReadOnlyList<DingTalkDirectoryEntry> entries, CancellationToken cancellationToken = default);
    Task<AccessPerson> CreateVisitorAsync(string name, DateTime beginUtc, DateTime endUtc, string? cardNo, string? password, Guid? faceAssetId, bool generateQr, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonIssueResult>> UpdateVisitorAsync(Guid id, string name, DateTime beginUtc, DateTime endUtc, string? cardNo, string? password, Guid? faceAssetId, bool generateQr, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonIssueResult>> ReissueVisitorAsync(Guid id, string? password, CancellationToken cancellationToken = default);
    Task RevokeVisitorQrAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PersonIssueResult>> UpdateCredentialsAsync(Guid id, string? cardNo, string? password, Guid? faceAssetId, CancellationToken cancellationToken = default);
    Task PublishToHikiotAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class PersonService(AccessItDbContext db, IHikiotGateway hikiot, IOptions<HikiotOptions> hikiotOptions) : IPersonService
{
    private static readonly SemaphoreSlim SyncGate = new(1, 1);

    public async Task<SyncResult> SyncHikiotAsync(CancellationToken cancellationToken = default)
    {
        await SyncGate.WaitAsync(cancellationToken);
        try
        {
        var created = 0; var updated = 0;
        foreach (var remote in await hikiot.GetTeamPeopleAsync(cancellationToken))
        {
            var (person, wasCreated) = await FindOrCreateAsync(remote.Name, remote.Mobile, AccessPersonKind.Employee, PersonSourceType.Hikiot, remote.PersonNo, null, cancellationToken);
            person.HikiotPersonNo = remote.PersonNo;
            var identifications = await hikiot.GetTeamIdentificationsAsync(remote.PersonNo, cancellationToken);
            var remoteCards = identifications.Where(x => x.Type == 1).Select(x => x.Value);
            AddCards(person, remoteCards);
            person.CardNo = remoteCards.FirstOrDefault() ?? person.CardNo;
            person.HikiotFaceUrl = identifications.FirstOrDefault(x => x.Type == 3)?.Value;
            person.UpdatedAtUtc = DateTime.UtcNow;
            if (wasCreated) created++; else updated++;
            // 海康团队凭证接口上限为 10 次/秒，避免同步多人时被限流。
            await Task.Delay(110, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return new SyncResult(created, updated);
        }
        finally { SyncGate.Release(); }
    }

    public async Task<SyncResult> SyncDingTalkAsync(IReadOnlyList<DingTalkDirectoryEntry> entries, CancellationToken cancellationToken = default)
    {
        await SyncGate.WaitAsync(cancellationToken);
        try
        {
        var created = 0; var updated = 0;
        foreach (var entry in entries.Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.UserId)))
        {
            var (person, wasCreated) = await FindOrCreateAsync(entry.Name, entry.Mobile, AccessPersonKind.Employee, PersonSourceType.DingTalk, entry.UserId, entry.UnionId, cancellationToken);
            person.UpdatedAtUtc = DateTime.UtcNow;
            if (wasCreated) created++; else updated++;
        }
        await db.SaveChangesAsync(cancellationToken);
        return new SyncResult(created, updated);
        }
        finally { SyncGate.Release(); }
    }

    public async Task<AccessPerson> CreateVisitorAsync(string name, DateTime beginUtc, DateTime endUtc, string? cardNo, string? password, Guid? faceAssetId, bool generateQr, CancellationToken cancellationToken = default)
    {
        if (endUtc <= beginUtc) throw new InvalidOperationException("访客结束时间必须晚于开始时间。");
        var person = new AccessPerson
        {
            Name = name.Trim(), NormalizedName = Normalize(name), Kind = AccessPersonKind.Visitor,
            DeviceEmployeeNo = "V" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant(),
            EnableBeginTimeUtc = beginUtc, EnableEndTimeUtc = endUtc, CardNo = string.IsNullOrWhiteSpace(cardNo) ? null : cardNo.Trim(), FaceAssetId = faceAssetId,
            QrShareToken = generateQr ? NewToken() : null
        };
        AddCards(person, ParseCards(cardNo));
        db.AccessPeople.Add(person);
        await db.SaveChangesAsync(cancellationToken);
        var results = await IssueDirectAsync(person, password, generateQr, cancellationToken);
        person.LastIssueResultJson = JsonSerializer.Serialize(results);
        await db.SaveChangesAsync(cancellationToken);
        return person;
    }

    public async Task<IReadOnlyList<PersonIssueResult>> UpdateCredentialsAsync(Guid id, string? cardNo, string? password, Guid? faceAssetId, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.Include(x => x.FaceAsset).Include(x => x.Cards).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("人员不存在。");
        if (cardNo is not null) { AddCards(person, ParseCards(cardNo)); person.CardNo = person.Cards.FirstOrDefault()?.CardNo; }
        if (faceAssetId.HasValue) person.FaceAssetId = faceAssetId;
        person.UpdatedAtUtc = DateTime.UtcNow;
        var results = new List<PersonIssueResult>();
        if (person.Kind == AccessPersonKind.Visitor)
            results.AddRange(await IssueDirectAsync(person, password, false, cancellationToken));
        else
        {
            if (string.IsNullOrWhiteSpace(person.HikiotPersonNo)) throw new InvalidOperationException("该人员尚未在海康团队中创建，不能修改团队认证信息。");
            if (cardNo is not null && !string.IsNullOrWhiteSpace(person.CardNo)) await hikiot.AddTeamCardAsync(person.HikiotPersonNo, person.CardNo, cancellationToken);
            if (faceAssetId.HasValue)
            {
                var face = await db.FaceAssets.FindAsync([faceAssetId.Value], cancellationToken) ?? throw new InvalidOperationException("人脸图片不存在。");
                await hikiot.AddTeamFaceAsync(person.HikiotPersonNo, BuildFaceUrl(face.PublicToken), cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(password)) results.AddRange(await IssueDirectAsync(person, password, false, cancellationToken));
        }
        person.LastIssueResultJson = JsonSerializer.Serialize(results);
        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<IReadOnlyList<PersonIssueResult>> UpdateVisitorAsync(Guid id, string name, DateTime beginUtc, DateTime endUtc, string? cardNo, string? password, Guid? faceAssetId, bool generateQr, CancellationToken cancellationToken = default)
    {
        if (endUtc <= beginUtc) throw new InvalidOperationException("访客结束时间必须晚于开始时间。");
        var person = await db.AccessPeople.Include(x => x.Cards).SingleOrDefaultAsync(x => x.Id == id && x.Kind == AccessPersonKind.Visitor, cancellationToken) ?? throw new KeyNotFoundException("访客不存在。");
        person.Name = name.Trim(); person.NormalizedName = Normalize(name); person.EnableBeginTimeUtc = beginUtc; person.EnableEndTimeUtc = endUtc;
        AddCards(person, ParseCards(cardNo));
        person.CardNo = person.Cards.FirstOrDefault()?.CardNo;
        person.FaceAssetId = faceAssetId;
        if (generateQr && string.IsNullOrWhiteSpace(person.QrShareToken)) { person.QrShareToken = NewToken(); person.QrRevokedAtUtc = null; person.QrContent = null; }
        if (!generateQr) { person.QrShareToken = null; person.QrContent = null; person.QrRevokedAtUtc = DateTime.UtcNow; }
        person.UpdatedAtUtc = DateTime.UtcNow;
        var results = await IssueDirectAsync(person, password, generateQr, cancellationToken);
        person.LastIssueResultJson = JsonSerializer.Serialize(results);
        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<IReadOnlyList<PersonIssueResult>> ReissueVisitorAsync(Guid id, string? password, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.SingleOrDefaultAsync(x => x.Id == id && x.Kind == AccessPersonKind.Visitor, cancellationToken) ?? throw new KeyNotFoundException("访客不存在。");
        var results = await IssueDirectAsync(person, password, !string.IsNullOrWhiteSpace(person.QrShareToken) && person.QrRevokedAtUtc is null, cancellationToken);
        person.LastIssueResultJson = JsonSerializer.Serialize(results);
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task RevokeVisitorQrAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.SingleOrDefaultAsync(x => x.Id == id && x.Kind == AccessPersonKind.Visitor, cancellationToken) ?? throw new KeyNotFoundException("访客不存在。");
        person.QrRevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PublishToHikiotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var person = await db.AccessPeople.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("人员不存在。");
        if (person.Kind != AccessPersonKind.Employee) throw new InvalidOperationException("访客不能创建为海康团队人员。");
        if (!string.IsNullOrWhiteSpace(person.HikiotPersonNo)) return;
        person.HikiotPersonNo = await hikiot.CreateTeamPersonAsync(person.Name, person.Mobile, cancellationToken);
        db.PersonSources.Add(new PersonSource { AccessPersonId = person.Id, SourceType = PersonSourceType.Hikiot, ExternalId = person.HikiotPersonNo });
        person.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<PersonIssueResult>> IssueDirectAsync(AccessPerson person, string? password, bool generateQr, CancellationToken cancellationToken)
    {
        var devices = await hikiot.GetDoorDevicesAsync(cancellationToken);
        if (devices.Count == 0) throw new InvalidOperationException("未发现支持人员信息的门禁设备。");
        var begin = person.EnableBeginTimeUtc ?? DateTime.UtcNow;
        var end = person.EnableEndTimeUtc ?? DateTime.UtcNow.AddYears(1);
        var results = new List<PersonIssueResult>();
        foreach (var device in devices)
        {
            var user = await hikiot.UpsertDirectUserAsync(device, new HikiotDirectUser(person.DeviceEmployeeNo, person.Name, person.Kind == AccessPersonKind.Visitor, person.PermanentValid, begin, end, password), cancellationToken);
            results.Add(new PersonIssueResult(device.Serial, user.Succeeded, user.Message));
            if (!user.Succeeded) continue;
            var cards = person.Cards.Select(x => x.CardNo).DefaultIfEmpty(person.CardNo).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
            if (device.SupportsCard)
            {
                foreach (var cardNo in cards)
                {
                    var card = await hikiot.UpsertDirectCardAsync(device, person.DeviceEmployeeNo, cardNo!, cancellationToken);
                    results.Add(new PersonIssueResult(device.Serial, card.Succeeded, "卡片：" + card.Message));
                }
            }
            if (person.FaceAssetId.HasValue && device.SupportsFace)
            {
                var face = await db.FaceAssets.FindAsync([person.FaceAssetId.Value], cancellationToken);
                if (face is not null)
                {
                    var item = await hikiot.UpsertDirectFaceAsync(device, person.DeviceEmployeeNo, BuildFaceUrl(face.PublicToken), cancellationToken);
                    results.Add(new PersonIssueResult(device.Serial, item.Succeeded, "人脸：" + item.Message));
                }
            }
            if (generateQr && device.SupportsCard)
            {
                var virtualCard = person.CardNo ?? ("Q" + person.Id.ToString("N")[..10].ToUpperInvariant());
                if (string.IsNullOrWhiteSpace(person.CardNo))
                {
                    var card = await hikiot.UpsertDirectCardAsync(device, person.DeviceEmployeeNo, virtualCard, cancellationToken);
                    results.Add(new PersonIssueResult(device.Serial, card.Succeeded, "二维码虚拟卡：" + card.Message));
                    if (!card.Succeeded) continue;
                }
                var qr = await hikiot.GenerateVisitorQrAsync(device, person.DeviceEmployeeNo, virtualCard, cancellationToken);
                person.QrContent ??= qr;
            }
        }
        return results;
    }

    private async Task<(AccessPerson Person, bool Created)> FindOrCreateAsync(string name, string? mobile, AccessPersonKind kind, PersonSourceType sourceType, string externalId, string? unionId, CancellationToken cancellationToken)
    {
        var source = await db.PersonSources.Include(x => x.AccessPerson).ThenInclude(x => x.Cards).SingleOrDefaultAsync(x => x.SourceType == sourceType && x.ExternalId == externalId, cancellationToken);
        var person = source?.AccessPerson ?? await db.AccessPeople.Include(x => x.Cards).FirstOrDefaultAsync(x => x.NormalizedName == Normalize(name), cancellationToken);
        var created = person is null;
        if (person is null)
        {
            person = new AccessPerson { Name = name.Trim(), NormalizedName = Normalize(name), Mobile = mobile, Kind = kind, DeviceEmployeeNo = "E" + Guid.NewGuid().ToString("N")[..20].ToUpperInvariant() };
            db.AccessPeople.Add(person);
        }
        person.Mobile ??= mobile;
        if (source is null)
            db.PersonSources.Add(new PersonSource { AccessPerson = person, SourceType = sourceType, ExternalId = externalId, UnionId = unionId });
        else
        {
            source.UnionId ??= unionId;
            source.SyncedAtUtc = DateTime.UtcNow;
        }
        return (person, created);
    }

    private string BuildFaceUrl(string token) => hikiotOptions.Value.PublicBaseUrl.TrimEnd('/') + "/public/faces/" + token;
    private static IEnumerable<string> ParseCards(string? value) => (value ?? string.Empty).Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase);
    private static void AddCards(AccessPerson person, IEnumerable<string> values)
    {
        foreach (var cardNo in values.Where(x => !string.IsNullOrWhiteSpace(x)))
            if (!person.Cards.Any(x => string.Equals(x.CardNo, cardNo, StringComparison.OrdinalIgnoreCase))) person.Cards.Add(new AccessCard { CardNo = cardNo });
    }
    public static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    private static string NewToken() => Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
