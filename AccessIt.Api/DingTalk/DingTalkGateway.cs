using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;

namespace AccessIt.Api.DingTalk;

public sealed class DingTalkGateway(HttpClient client, IMemoryCache cache, IOptions<DingTalkOptions> options) : IDingTalkGateway
{
    private const string AppTokenCacheKey = "AccessIt.DingTalk.AppToken";
    private readonly DingTalkOptions _options = options.Value;

    public async Task<DingTalkProfile> GetWebProfileAsync(string code, CancellationToken cancellationToken = default)
    {
        var token = await client.PostAsJsonAsync("https://api.dingtalk.com/v1.0/oauth2/userAccessToken", new
        {
            clientId = _options.AppKey,
            clientSecret = _options.AppSecret,
            code,
            grantType = "authorization_code"
        }, cancellationToken);
        token.EnsureSuccessStatusCode();
        var tokenData = await token.Content.ReadFromJsonAsync<DingTalkUserToken>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("DingTalk did not return a user token.");
        if (string.IsNullOrWhiteSpace(tokenData.AccessToken)) throw new InvalidOperationException("DingTalk returned an empty user token.");

        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.dingtalk.com/v1.0/contact/users/me");
        request.Headers.Add("x-acs-dingtalk-access-token", tokenData.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var profile = await response.Content.ReadFromJsonAsync<DingTalkMe>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("DingTalk did not return profile data.");
        return new DingTalkProfile(profile.OpenId ?? profile.UnionId ?? throw new InvalidOperationException("DingTalk profile has no user identifier."), profile.UnionId, profile.Nick, profile.Mobile);
    }

    public async Task<DingTalkProfile> GetInAppProfileAsync(string code, CancellationToken cancellationToken = default)
    {
        var appToken = await GetAppTokenAsync(cancellationToken);
        var userInfo = await client.PostAsJsonAsync($"https://oapi.dingtalk.com/topapi/v2/user/getuserinfo?access_token={Uri.EscapeDataString(appToken)}", new { code }, cancellationToken);
        userInfo.EnsureSuccessStatusCode();
        var response = await userInfo.Content.ReadFromJsonAsync<DingTalkResponse<DingTalkLegacyUser>>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("DingTalk did not return in-app profile data.");
        EnsureSuccess(response, "Unable to obtain DingTalk in-app user.");
        var userId = response.Result?.UserId ?? throw new InvalidOperationException("DingTalk in-app profile has no user ID.");
        return await GetDirectoryProfileAsync(appToken, userId, cancellationToken);
    }

    public async Task<IReadOnlyList<DingTalkDirectoryEntry>> GetDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var appToken = await GetAppTokenAsync(cancellationToken);
        var departmentIds = new HashSet<long> { 1 };
        var pending = new Queue<long>(departmentIds);
        while (pending.TryDequeue(out var departmentId))
        {
            var result = await PostApiAsync<List<DingTalkDepartment>>(appToken, "/topapi/v2/department/listsub", new { dept_id = departmentId, language = "zh_CN" }, cancellationToken);
            foreach (var department in result)
                if (departmentIds.Add(department.DeptId)) pending.Enqueue(department.DeptId);
        }

        var userIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var departmentId in departmentIds)
        {
            var result = await PostApiAsync<DingTalkUserIdList>(appToken, "/topapi/user/listid", new { dept_id = departmentId }, cancellationToken);
            foreach (var userId in result.UserIds) userIds.Add(userId);
        }

        var entries = new List<DingTalkDirectoryEntry>();
        foreach (var userId in userIds)
        {
            var user = await PostApiAsync<DingTalkDirectoryUser>(appToken, "/topapi/v2/user/get", new { userid = userId, language = "zh_CN" }, cancellationToken);
            if (!string.IsNullOrWhiteSpace(user.Name))
                entries.Add(new DingTalkDirectoryEntry(user.UserId ?? userId, user.UnionId, user.Name, user.Mobile, user.Active ?? true));
        }
        return entries.OrderBy(x => x.Name).ThenBy(x => x.UserId).ToList();
    }

    public async Task SendWorkNoticeAsync(IEnumerable<string> userIds, string content, CancellationToken cancellationToken = default)
    {
        if (!_options.AgentId.HasValue) throw new InvalidOperationException("DingTalk:AgentId is not configured.");
        var token = await GetAppTokenAsync(cancellationToken);
        foreach (var chunk in userIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Chunk(100))
        {
            var result = await client.PostAsJsonAsync($"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={Uri.EscapeDataString(token)}", new
            {
                agent_id = _options.AgentId.Value,
                userid_list = string.Join(',', chunk),
                msg = new { msgtype = "text", text = new { content } }
            }, cancellationToken);
            result.EnsureSuccessStatusCode();
            var body = await result.Content.ReadFromJsonAsync<DingTalkResponse<object>>(cancellationToken: cancellationToken);
            EnsureSuccess(body, "DingTalk work notice failed.");
        }
    }

    private async Task<string> GetAppTokenAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(AppTokenCacheKey, out string? cached) && !string.IsNullOrWhiteSpace(cached)) return cached;
        if (string.IsNullOrWhiteSpace(_options.AppKey) || string.IsNullOrWhiteSpace(_options.AppSecret)) throw new InvalidOperationException("DingTalk app credentials are not configured.");
        var response = await client.PostAsJsonAsync("https://api.dingtalk.com/v1.0/oauth2/accessToken", new { appKey = _options.AppKey, appSecret = _options.AppSecret }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<DingTalkAppToken>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("DingTalk did not return an app token.");
        if (string.IsNullOrWhiteSpace(data.AccessToken)) throw new InvalidOperationException("DingTalk returned an empty app token.");
        cache.Set(AppTokenCacheKey, data.AccessToken, TimeSpan.FromSeconds(Math.Max(60, data.ExpireIn - 120)));
        return data.AccessToken;
    }

    private async Task<DingTalkProfile> GetDirectoryProfileAsync(string appToken, string userId, CancellationToken cancellationToken)
    {
        var user = await PostApiAsync<DingTalkDirectoryUser>(appToken, "/topapi/v2/user/get", new { userid = userId, language = "zh_CN" }, cancellationToken);
        return new DingTalkProfile(user.UserId ?? userId, user.UnionId, user.Name, user.Mobile);
    }

    private async Task<T> PostApiAsync<T>(string appToken, string path, object body, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync($"https://oapi.dingtalk.com{path}?access_token={Uri.EscapeDataString(appToken)}", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<DingTalkResponse<T>>(cancellationToken: cancellationToken);
        EnsureSuccess(envelope, $"DingTalk request {path} failed.");
        return envelope!.Result ?? throw new InvalidOperationException($"DingTalk request {path} returned no result.");
    }

    private static void EnsureSuccess<T>(DingTalkResponse<T>? response, string context)
    {
        if (response is null || response.ErrorCode != 0) throw new InvalidOperationException($"{context} {response?.ErrorMessage}");
    }
}

public sealed class DingTalkResponse<T>
{
    [JsonPropertyName("errcode")] public int ErrorCode { get; init; }
    [JsonPropertyName("errmsg")] public string? ErrorMessage { get; init; }
    [JsonPropertyName("result")] public T? Result { get; init; }
}
public sealed class DingTalkUserToken { [JsonPropertyName("accessToken")] public string? AccessToken { get; init; } }
public sealed class DingTalkAppToken { [JsonPropertyName("accessToken")] public string? AccessToken { get; init; } [JsonPropertyName("expireIn")] public int ExpireIn { get; init; } }
public sealed class DingTalkMe { [JsonPropertyName("nick")] public string Nick { get; init; } = string.Empty; [JsonPropertyName("openId")] public string? OpenId { get; init; } [JsonPropertyName("unionId")] public string? UnionId { get; init; } [JsonPropertyName("mobile")] public string? Mobile { get; init; } }
public sealed class DingTalkLegacyUser { [JsonPropertyName("userid")] public string? UserId { get; init; } }
public sealed class DingTalkDepartment { [JsonPropertyName("dept_id")] public long DeptId { get; init; } }
public sealed class DingTalkUserIdList { [JsonPropertyName("userid_list")] public List<string> UserIds { get; init; } = []; }
public sealed class DingTalkDirectoryUser { [JsonPropertyName("userid")] public string? UserId { get; init; } [JsonPropertyName("unionid")] public string? UnionId { get; init; } [JsonPropertyName("name")] public string Name { get; init; } = string.Empty; [JsonPropertyName("mobile")] public string? Mobile { get; init; } [JsonPropertyName("active")] public bool? Active { get; init; } }
