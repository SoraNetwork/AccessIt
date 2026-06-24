using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AccessIt.Api.Configuration;
using AccessIt.Api.Data;
using AccessIt.Api.Domain;
using AccessIt.Api.Security;

namespace AccessIt.Api.Hikiot;

/// <summary>
/// HIKIoT 网关 —— 重构后只保留 App/User Token 的获取、缓存与自动刷新，以及第三方 OAuth 授权闭环。
/// <para>
/// 这是整个 HIKIoT 集成的"总闸"：所有后续业务 API 都必须先经过 <see cref="GetAuthorizedTokensAsync"/>
/// 拿到有效的 App-Access-Token 与 User-Access-Token。业务方法已在本次清理中移除，重新开发时
/// 在本类中按需新增（复用 <see cref="GetSecureAsync{T}"/> / <see cref="PostSecureAsync{T}"/> 即可自动带上刷新后的 token）。
/// </para>
/// </summary>
public class HikiotGateway(
    AccessItDbContext db,
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector,
    IOptions<HikiotOptions> options,
    TimeProvider timeProvider) : IHikiotGateway
{
    /// <summary>串行化 token 刷新，避免并发请求重复换 token。</summary>
    private static readonly SemaphoreSlim TokenGate = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // HIKIoT 的 token 响应会把 expiresIn 返回为字符串，例如 "7200"。
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HikiotOptions _options = options.Value;

    public async Task<HikiotConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return new HikiotConnectionStatus(
            !connection.NeedsReauthorization && connection.UserTokenExpiresAtUtc > timeProvider.GetUtcNow(),
            connection.NeedsReauthorization,
            connection.TeamNo,
            connection.DefaultDepartmentNo,
            connection.UserTokenExpiresAtUtc,
            connection.LastError);
    }

    public async Task<string> BeginAuthorizationAsync(string requestedByUserId, CancellationToken cancellationToken = default)
    {
        ValidateSetup();
        var state = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());
        db.HikiotAuthorizationStates.Add(new HikiotAuthorizationState
        {
            State = state,
            RequestedByUserId = requestedByUserId,
            ExpiresAtUtc = timeProvider.GetUtcNow().AddMinutes(10).UtcDateTime
        });
        await db.SaveChangesAsync(cancellationToken);

        return $"https://open.hikiot.com/oauth/thirdpart?state={Uri.EscapeDataString(state)}&appKey={Uri.EscapeDataString(_options.AppKey)}&redirectUrl={Uri.EscapeDataString(_options.RedirectUri)}";
    }

    public async Task CompleteAuthorizationAsync(string state, string authCode, CancellationToken cancellationToken = default)
    {
        ValidateSetup();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var authorization = await db.HikiotAuthorizationStates.SingleOrDefaultAsync(x => x.State == state, cancellationToken);
        if (authorization is null || authorization.ExpiresAtUtc < now)
            throw new InvalidOperationException("HIKIoT authorization state is invalid or expired.");

        // code2Token 只需 App Token，无需 User Token。
        var appToken = await GetAppTokenAsync(cancellationToken);
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/auth/third/code2Token?authCode={Uri.EscapeDataString(authCode)}");
        request.Headers.Add("App-Access-Token", appToken);
        var result = await SendAsync<HikiotTokenData>(client, request, cancellationToken);
        if (result.Code != 0 || string.IsNullOrWhiteSpace(result.Data?.UserAccessToken))
            throw new InvalidOperationException($"HIKIoT user authorization failed: {result.Message}");

        var connection = await GetConnectionAsync(cancellationToken);
        connection.TeamNo = result.Data.TeamNo;
        connection.AccountNo = result.Data.AccountNo;
        connection.AuthorizedByUserId = authorization.RequestedByUserId;
        connection.ProtectedUserAccessToken = secretProtector.Protect(result.Data.UserAccessToken);
        connection.ProtectedRefreshUserToken = secretProtector.Protect(result.Data.RefreshUserToken ?? string.Empty);
        connection.UserTokenExpiresAtUtc = now.AddDays(Math.Max(1, result.Data.ExpiresIn));
        connection.AuthorizedAtUtc = now;
        connection.NeedsReauthorization = false;
        connection.LastError = null;
        db.HikiotAuthorizationStates.Remove(authorization);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDefaultDepartmentAsync(string departmentNo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(departmentNo)) throw new InvalidOperationException("请填写海康根部门编号（departNo）。");
        var connection = await GetConnectionAsync(cancellationToken);
        connection.DefaultDepartmentNo = departmentNo.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    //  Token 缓存机制（AT/UT）—— 保留，供后续业务方法复用
    // ─────────────────────────────────────────────────────────────────────────────
    //  设计要点：
    //  1) App Token 与 User Token 都持久化在 HikiotConnection 单行（Id=1），密文存储（SecretProtector）。
    //  2) App Token：剩余有效期 < 5 分钟时刷新，先尝试 refreshAppToken，失败回退到 exchangeAppToken。
    //  3) User Token：剩余有效期 < 1 天时刷新，用 refreshUserAccessToken；失败则置 NeedsReauthorization=true，
    //     必须人工重新走 OAuth 授权。
    //  4) TokenGate 串行化，确保同一时刻只有一个刷新请求。
    //  5) GetSecureAsync/PostSecureAsync 是所有业务请求的统一入口，自动注入双 token。

    /// <summary>
    /// 业务 GET 请求的统一入口：自动获取（并按需刷新）双 token 后发起请求。
    /// 重新开发业务 API 时，新增的 GET 方法应统一走这里。
    /// </summary>
    protected internal async Task<HikiotEnvelope<T>> GetSecureAsync<T>(string relativePath, CancellationToken cancellationToken = default)
    {
        var tokens = await GetAuthorizedTokensAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        AddTokens(request, tokens);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
    }

    /// <summary>
    /// 业务 POST 请求的统一入口：自动获取（并按需刷新）双 token 后发起请求。
    /// 重新开发业务 API 时，新增的 POST 方法应统一走这里。
    /// </summary>
    protected internal async Task<HikiotEnvelope<T>> PostSecureAsync<T>(string relativePath, object body, CancellationToken cancellationToken = default)
    {
        var tokens = await GetAuthorizedTokensAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = JsonContent.Create(body) };
        AddTokens(request, tokens);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
    }

    /// <summary>取已授权的双 token，并在过期前自动刷新 User Token。</summary>
    private async Task<(string AppToken, string UserToken)> GetAuthorizedTokensAsync(CancellationToken cancellationToken)
    {
        var appToken = await GetAppTokenAsync(cancellationToken);
        var connection = await GetConnectionAsync(cancellationToken);
        if (connection.NeedsReauthorization || string.IsNullOrWhiteSpace(connection.ProtectedUserAccessToken))
            throw new InvalidOperationException("HIKIoT requires user authorization.");

        if (connection.UserTokenExpiresAtUtc <= timeProvider.GetUtcNow().AddDays(1).UtcDateTime)
        {
            await TokenGate.WaitAsync(cancellationToken);
            try
            {
                connection = await GetConnectionAsync(cancellationToken);
                if (connection.UserTokenExpiresAtUtc <= timeProvider.GetUtcNow().AddDays(1).UtcDateTime)
                {
                    var refreshToken = secretProtector.Unprotect(connection.ProtectedRefreshUserToken);
                    var userToken = secretProtector.Unprotect(connection.ProtectedUserAccessToken);
                    if (string.IsNullOrWhiteSpace(refreshToken) || string.IsNullOrWhiteSpace(userToken))
                        throw new InvalidOperationException("HIKIoT requires user reauthorization.");
                    var refreshResponse = await PostPublicAsync<HikiotTokenData>("/auth/third/refreshUserAccessToken", new { userAccessToken = userToken, refreshUserToken = refreshToken }, appToken, cancellationToken);
                    if (refreshResponse.Code != 0 || string.IsNullOrWhiteSpace(refreshResponse.Data?.UserAccessToken))
                    {
                        connection.NeedsReauthorization = true;
                        connection.LastError = refreshResponse.Message;
                        connection.LastErrorAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                        await db.SaveChangesAsync(cancellationToken);
                        throw new InvalidOperationException("HIKIoT user authorization has expired.");
                    }
                    connection.ProtectedUserAccessToken = secretProtector.Protect(refreshResponse.Data.UserAccessToken);
                    connection.ProtectedRefreshUserToken = secretProtector.Protect(refreshResponse.Data.RefreshUserToken ?? refreshToken);
                    connection.UserTokenExpiresAtUtc = timeProvider.GetUtcNow().AddDays(Math.Max(1, refreshResponse.Data.ExpiresIn)).UtcDateTime;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            finally { TokenGate.Release(); }
        }
        return (appToken, secretProtector.Unprotect(connection.ProtectedUserAccessToken)!);
    }

    /// <summary>获取 App Token：缓存未过期直接返回，否则刷新（先 refresh 后 exchange）。</summary>
    private async Task<string> GetAppTokenAsync(CancellationToken cancellationToken)
    {
        ValidateSetup();
        var connection = await GetConnectionAsync(cancellationToken);
        if (connection.AppTokenExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(5).UtcDateTime && !string.IsNullOrWhiteSpace(connection.ProtectedAppAccessToken))
            return secretProtector.Unprotect(connection.ProtectedAppAccessToken)!;

        await TokenGate.WaitAsync(cancellationToken);
        try
        {
            connection = await GetConnectionAsync(cancellationToken);
            if (connection.AppTokenExpiresAtUtc > timeProvider.GetUtcNow().AddMinutes(5).UtcDateTime && !string.IsNullOrWhiteSpace(connection.ProtectedAppAccessToken))
                return secretProtector.Unprotect(connection.ProtectedAppAccessToken)!;

            if (!string.IsNullOrWhiteSpace(connection.ProtectedAppAccessToken) && !string.IsNullOrWhiteSpace(connection.ProtectedRefreshAppToken))
            {
                var currentAppToken = secretProtector.Unprotect(connection.ProtectedAppAccessToken);
                var refreshAppToken = secretProtector.Unprotect(connection.ProtectedRefreshAppToken);
                if (!string.IsNullOrWhiteSpace(currentAppToken) && !string.IsNullOrWhiteSpace(refreshAppToken))
                {
                    var refreshed = await PostPublicAsync<HikiotTokenData>("/auth/refreshAppToken", new { appAccessToken = currentAppToken, refreshAppToken }, null, cancellationToken);
                    if (refreshed.Code == 0 && !string.IsNullOrWhiteSpace(refreshed.Data?.AppAccessToken))
                    {
                        connection.ProtectedAppAccessToken = secretProtector.Protect(refreshed.Data.AppAccessToken);
                        connection.ProtectedRefreshAppToken = secretProtector.Protect(refreshed.Data.RefreshAppToken ?? refreshAppToken);
                        connection.AppTokenExpiresAtUtc = timeProvider.GetUtcNow().AddHours(Math.Max(1, refreshed.Data.ExpiresIn)).UtcDateTime;
                        await db.SaveChangesAsync(cancellationToken);
                        return refreshed.Data.AppAccessToken;
                    }
                }
            }

            var response = await PostPublicAsync<HikiotTokenData>("/auth/exchangeAppToken", new { appKey = _options.AppKey, appSecret = _options.AppSecret }, null, cancellationToken);
            if (response.Code != 0 || string.IsNullOrWhiteSpace(response.Data?.AppAccessToken))
                throw new InvalidOperationException($"Unable to obtain HIKIoT app token: {response.Message}");

            connection.ProtectedAppAccessToken = secretProtector.Protect(response.Data.AppAccessToken);
            connection.ProtectedRefreshAppToken = secretProtector.Protect(response.Data.RefreshAppToken ?? string.Empty);
            connection.AppTokenExpiresAtUtc = timeProvider.GetUtcNow().AddHours(Math.Max(1, response.Data.ExpiresIn)).UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return response.Data.AppAccessToken;
        }
        finally { TokenGate.Release(); }
    }

    /// <summary>无需 User Token 的公开 POST（仅 token 接口自身使用）。</summary>
    private async Task<HikiotEnvelope<T>> PostPublicAsync<T>(string relativePath, object body, string? appAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(appAccessToken)) request.Headers.Add("App-Access-Token", appAccessToken);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
    }

    public async Task<IReadOnlyList<HikiotTeamPerson>> GetTeamPeopleAsync(CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var result = new Dictionary<string, HikiotTeamPerson>(StringComparer.OrdinalIgnoreCase);
        const int teamPageSize = 50;
        foreach (var departmentNo in await GetAllDepartmentNosAsync(connection, cancellationToken))
        {
            for (var page = 1; page <= 100; page++)
            {
                var response = await GetSecureAsync<JsonElement>($"/team/v1/person/page?departNo={Uri.EscapeDataString(departmentNo)}&page={page}&size={teamPageSize}", cancellationToken);
                EnsureApiSuccess(response, "读取海康团队人员失败");
                var entries = GetArray(response.Data, "list", "records", "items", "data");
                foreach (var item in entries)
                {
                    var personNo = GetString(item, "personNo", "person_no", "id");
                    var name = GetString(item, "name", "personName");
                    if (!string.IsNullOrWhiteSpace(personNo) && !string.IsNullOrWhiteSpace(name)) result[personNo] = new HikiotTeamPerson(personNo, name, GetString(item, "phone", "mobile"));
                }
                if (entries.Count < teamPageSize) break;
            }
        }
        return result.Values.OrderBy(x => x.Name).ToList();
    }

    public async Task<string> CreateTeamPersonAsync(string name, string? mobile, CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var departmentNo = await GetRootDepartmentNoAsync(connection, cancellationToken);
        var response = await PostSecureAsync<JsonElement>("/team/v1/person/add", new { name, phone = mobile, departNo = departmentNo }, cancellationToken);
        EnsureApiSuccess(response, "创建海康团队人员失败");
        var personNo = GetString(response.Data, "personNo", "person_no", "id");
        if (string.IsNullOrWhiteSpace(personNo)) throw new InvalidOperationException("海康未返回人员编号。");
        return personNo;
    }

    public async Task<IReadOnlyList<HikiotIdentification>> GetTeamIdentificationsAsync(string personNo, CancellationToken cancellationToken = default)
    {
        var response = await GetSecureAsync<JsonElement>($"/team/v1/person/listIdentifications?personNo={Uri.EscapeDataString(personNo)}", cancellationToken);
        EnsureApiSuccess(response, "读取海康人员认证信息失败");
        var result = new List<HikiotIdentification>();
        foreach (var item in GetArray(response.Data, "identifications", "personIdentifications", "list", "items", "data"))
        {
            var typeText = GetString(item, "identificationType", "type");
            var value = GetString(item, "identification", "identificationValue", "value", "faceUrl");
            if (int.TryParse(typeText, out var type) && !string.IsNullOrWhiteSpace(value)) result.Add(new HikiotIdentification(type, value));
        }
        return result;
    }

    public async Task AddTeamCardAsync(string personNo, string cardNo, CancellationToken cancellationToken = default)
    {
        var response = await PostSecureAsync<JsonElement>("/team/v1/person/addIdentification", new { personNo, identificationType = 1, identification = cardNo }, cancellationToken);
        EnsureApiSuccess(response, "保存海康团队卡片失败");
    }

    public async Task AddTeamFaceAsync(string personNo, string faceUrl, CancellationToken cancellationToken = default)
    {
        var detect = await PostSecureAsync<JsonElement>("/team/v1/person/faceDetect", new { faceUrl }, cancellationToken);
        EnsureApiSuccess(detect, "海康人脸质量检测失败");
        var response = await PostSecureAsync<JsonElement>("/team/v1/person/addIdentification", new { personNo, identificationType = 3, identification = faceUrl }, cancellationToken);
        EnsureApiSuccess(response, "保存海康团队人脸失败");
    }

    public async Task<IReadOnlyList<HikiotDoorDevice>> GetDoorDevicesAsync(CancellationToken cancellationToken = default)
    {
        var all = new Dictionary<string, HikiotDoorDevice>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; page <= 100; page++)
        {
            var groups = await GetSecureAsync<JsonElement>($"/issue/v1/deviceGroup/page?page={page}&size=100&containsDefault=true", cancellationToken);
            EnsureApiSuccess(groups, "读取门禁设备失败");
            var entries = GetArray(groups.Data, "list", "records", "items", "data", "deviceGroups", "deviceGroupVOs", "deviceGroupList");
            foreach (var item in entries)
            {
                foreach (var device in ExpandDevices(item))
                {
                    var serial = GetString(device, "deviceSerial", "resourceSerial", "serial", "deviceSn", "devSerial", "resourceCode");
                    if (string.IsNullOrWhiteSpace(serial)) continue;
                    var capacity = await GetSecureAsync<JsonElement>($"/issue/v1/device/capacityList?deviceSerial={Uri.EscapeDataString(serial)}", cancellationToken);
                    if (capacity.Code != 0) continue;
                    var text = capacity.Data.ValueKind == JsonValueKind.Undefined ? string.Empty : capacity.Data.GetRawText();
                    all[serial] = new HikiotDoorDevice(serial, GetString(device, "deviceName", "name") ?? serial,
                        HasCapability(text, "supportUserInfo"), HasCapability(text, "supportCard"), HasCapability(text, "supportFace"), HasCapability(text, "supportPurePwdVerify"));
                }
            }
            if (entries.Count < 100) break;
        }
        // 某些设备能力集把开关以字符串返回；若仍无法识别能力，保留已发现设备并让直连接口返回逐设备结果，
        // 不应因为能力集格式差异而让访客创建直接失败。
        var supported = all.Values.Where(x => x.SupportsUserInfo).ToList();
        return supported.Count > 0 ? supported : all.Values.ToList();
    }

    public async Task<DeviceIssueResult> UpsertDirectUserAsync(HikiotDoorDevice device, HikiotDirectUser user, CancellationToken cancellationToken = default)
    {
        var body = new { deviceSerial = device.Serial, payload = new { userInfo = new { employeeNo = user.EmployeeNo, name = user.Name, userType = user.IsVisitor ? "visitor" : "normal", permanentValid = user.PermanentValid, enableBeginTime = user.BeginUtc.ToString("yyyy-MM-dd'T'HH:mm:ss"), enableEndTime = user.EndUtc.ToString("yyyy-MM-dd'T'HH:mm:ss"), doorRightPlan = new[] { new { doorNo = 1, planTemplateId = new[] { 1 } }, }, doorRight = new[] { 1 }, password = device.SupportsPassword ? user.Password : null, maxOpenDoorTime = user.IsVisitor ? 0 : (int?)null } } };
        var response = await PostSecureAsync<JsonElement>("/device/direct/v1/userInfo/addOneRecord", body, cancellationToken);
        return ToIssueResult(device.Serial, response);
    }

    public async Task<DeviceIssueResult> UpsertDirectCardAsync(HikiotDoorDevice device, string employeeNo, string cardNo, CancellationToken cancellationToken = default)
    {
        var response = await PostSecureAsync<JsonElement>("/device/direct/v1/cardInfo/addOneRecord", new { deviceSerial = device.Serial, payload = new { cardInfo = new { employeeNo, cardNo } } }, cancellationToken);
        return ToIssueResult(device.Serial, response);
    }

    public async Task<DeviceIssueResult> UpsertDirectFaceAsync(HikiotDoorDevice device, string employeeNo, string faceUrl, CancellationToken cancellationToken = default)
    {
        var response = await PostSecureAsync<JsonElement>("/device/direct/v1/faceAccess/addOneRecord", new { deviceSerial = device.Serial, payload = new { faceAccessInfo = new { employeeNo, faceUrl, faceLibType = "blackFD" } } }, cancellationToken);
        return ToIssueResult(device.Serial, response);
    }

    public async Task<string?> GenerateVisitorQrAsync(HikiotDoorDevice device, string employeeNo, string cardNo, CancellationToken cancellationToken = default)
    {
        var response = await PostSecureAsync<JsonElement>("/device/direct/v1/qrCodeInfo/genQrCode", new { deviceSerial = device.Serial, payload = new { employeeNo, cardNo } }, cancellationToken);
        if (response.Code != 0) throw new InvalidOperationException($"设备 {device.Serial} 生成访客二维码失败：{response.Message}");
        return GetString(response.Data, "qrCode", "qrCodeInfo", "content", "data");
    }

    private static DeviceIssueResult ToIssueResult(string serial, HikiotEnvelope<JsonElement> response) => new(serial, response.Code == 0, response.Code == 0 ? "已接受下发" : $"{response.Code}: {response.Message}");
    private static void EnsureApiSuccess(HikiotEnvelope<JsonElement> response, string action) { if (response.Code != 0) throw new InvalidOperationException($"{action}：{response.Code} {response.Message}"); }
    private static bool HasCapability(string json, string name)
        => json.Contains($"\"{name}\":1", StringComparison.OrdinalIgnoreCase)
           || json.Contains($"\"{name}\":true", StringComparison.OrdinalIgnoreCase)
           || json.Contains($"\"{name}\":\"1\"", StringComparison.OrdinalIgnoreCase)
           || json.Contains($"\"{name}\":\"true\"", StringComparison.OrdinalIgnoreCase);
    private static List<JsonElement> GetArray(JsonElement data, params string[] names)
    {
        if (data.ValueKind == JsonValueKind.Array) return data.EnumerateArray().ToList();
        if (data.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names) if (data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().ToList();
            foreach (var property in data.EnumerateObject()) if (property.Value.ValueKind == JsonValueKind.Array) return property.Value.EnumerateArray().ToList();
        }
        return [];
    }
    private static IEnumerable<JsonElement> ExpandDevices(JsonElement item)
    {
        yield return item;
        if (item.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in item.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
                foreach (var child in property.Value.EnumerateArray())
                    foreach (var nested in ExpandDevices(child)) yield return nested;
            else if (property.Value.ValueKind == JsonValueKind.Object)
                foreach (var nested in ExpandDevices(property.Value)) yield return nested;
        }
    }
    private static string? GetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names) if (element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number) return value.ToString();
        return null;
    }
    private async Task<string> GetRootDepartmentNoAsync(HikiotConnection connection, CancellationToken cancellationToken)
    {
        var response = await GetSecureAsync<JsonElement>("/team/v1/depart/getDeparts", cancellationToken);
        EnsureApiSuccess(response, "读取海康根部门失败");
        var roots = GetArray(response.Data, "teamDepartVOs", "list", "records", "items");
        var root = roots.FirstOrDefault(x => GetString(x, "level") == "0");
        if (root.ValueKind == JsonValueKind.Undefined && roots.Count > 0) root = roots[0];
        var departmentNo = GetString(root, "departNo");
        if (string.IsNullOrWhiteSpace(departmentNo)) throw new InvalidOperationException("海康未返回根部门 departNo。");
        connection.DefaultDepartmentNo = departmentNo;
        await db.SaveChangesAsync(cancellationToken);
        return departmentNo;
    }

    private async Task<IReadOnlyList<string>> GetAllDepartmentNosAsync(HikiotConnection connection, CancellationToken cancellationToken)
    {
        var root = await GetRootDepartmentNoAsync(connection, cancellationToken);
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var parentNo))
        {
            var response = await GetSecureAsync<JsonElement>($"/team/v1/depart/getDeparts?departNo={Uri.EscapeDataString(parentNo)}", cancellationToken);
            EnsureApiSuccess(response, "读取海康子部门失败");
            foreach (var child in GetArray(response.Data, "teamDepartVOs", "list", "records", "items"))
            {
                var childNo = GetString(child, "departNo");
                if (!string.IsNullOrWhiteSpace(childNo) && all.Add(childNo)) pending.Enqueue(childNo);
            }
        }
        return all.ToList();
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("Hikiot");
        client.BaseAddress ??= new Uri(_options.ApiBaseUrl.TrimEnd('/') + "/");
        return client;
    }

    private static void AddTokens(HttpRequestMessage request, (string AppToken, string UserToken) tokens)
    {
        request.Headers.Add("App-Access-Token", tokens.AppToken);
        request.Headers.Add("User-Access-Token", tokens.UserToken);
    }

    private static async Task<HikiotEnvelope<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new HikiotEnvelope<T> { Code = (int)response.StatusCode, Message = response.ReasonPhrase ?? "HIKIoT HTTP failure", Detail = content };
        return JsonSerializer.Deserialize<HikiotEnvelope<T>>(content, JsonOptions)
               ?? new HikiotEnvelope<T> { Code = -1, Message = "HIKIoT returned an empty response." };
    }

    /// <summary>HIKIoT 连接单行（Id=1）。不存在时自动创建为待授权状态。</summary>
    private async Task<HikiotConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await db.HikiotConnections.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (connection is not null) return connection;
        connection = new HikiotConnection { Id = 1, NeedsReauthorization = true };
        db.HikiotConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    private void ValidateSetup()
    {
        if (string.IsNullOrWhiteSpace(_options.AppKey) || string.IsNullOrWhiteSpace(_options.AppSecret) || string.IsNullOrWhiteSpace(_options.RedirectUri))
            throw new InvalidOperationException("Hikiot AppKey, AppSecret, and RedirectUri must be configured.");
    }
}
