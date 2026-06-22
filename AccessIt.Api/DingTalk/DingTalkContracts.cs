namespace AccessIt.Api.DingTalk;

public sealed record DingTalkProfile(string UserId, string? UnionId, string Name, string? Mobile);
public sealed record DingTalkDirectoryEntry(string UserId, string? UnionId, string Name, string? Mobile, bool IsActive);

public interface IDingTalkGateway
{
    Task<DingTalkProfile> GetWebProfileAsync(string code, CancellationToken cancellationToken = default);
    Task<DingTalkProfile> GetInAppProfileAsync(string code, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DingTalkDirectoryEntry>> GetDirectoryAsync(CancellationToken cancellationToken = default);
    Task SendWorkNoticeAsync(IEnumerable<string> userIds, string content, CancellationToken cancellationToken = default);
}
