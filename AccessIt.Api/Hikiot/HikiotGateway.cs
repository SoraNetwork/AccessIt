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

public sealed class HikiotGateway(
    AccessItDbContext db,
    IHttpClientFactory httpClientFactory,
    ISecretProtector secretProtector,
    IOptions<HikiotOptions> options,
    TimeProvider timeProvider) : IHikiotGateway
{
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static readonly SemaphoreSlim TeamReadGate = new(1, 1);
    private static DateTimeOffset NextTeamReadAtUtc = DateTimeOffset.MinValue;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
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

    public async Task<IReadOnlyList<HikiotTeamDepartment>> GetTeamDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        var departments = new List<HikiotTeamDepartment>();
        var visitedParents = new HashSet<string?>(StringComparer.Ordinal);
        await ReadDepartmentsAsync(null, departments, visitedParents, cancellationToken);
        return departments;
    }

    public async Task SetDefaultDepartmentAsync(string departmentNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(departmentNo);
        var departments = await GetTeamDepartmentsAsync(cancellationToken);
        if (!departments.Any(x => x.DepartmentNo == departmentNo.Trim()))
            throw new InvalidOperationException("The selected HIKIoT department does not exist in the authorized team.");
        var connection = await GetConnectionAsync(cancellationToken);
        connection.DefaultDepartmentNo = departmentNo.Trim();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HikiotTeamPerson>> GetTeamPeopleAsync(CancellationToken cancellationToken = default)
    {
        var departments = await GetTeamDepartmentsAsync(cancellationToken);
        var people = new Dictionary<string, HikiotTeamPerson>(StringComparer.Ordinal);
        foreach (var department in departments.Where(x => x.HasPeople || x.IsLeaf).DistinctBy(x => x.DepartmentNo))
        {
            var page = 1;
            while (true)
            {
                await WaitForTeamReadSlotAsync(cancellationToken);
                var response = await GetSecureAsync<JsonElement>($"/team/v1/person/page?departNo={Uri.EscapeDataString(department.DepartmentNo)}&hasLeafDepart=true&page={page}&size=50", cancellationToken);
                EnsureSuccess(response);
                var current = ParseTeamPersonPage(response.Data);
                foreach (var person in current) if (!string.IsNullOrWhiteSpace(person.PersonNo)) people[person.PersonNo] = ToTeamPerson(person);
                if (current.Count < 50) break;
                page++;
            }
        }
        return people.Values.OrderBy(x => x.Name).ThenBy(x => x.PersonNo).ToList();
    }

    public async Task<HikiotTeamPerson?> GetTeamPersonAsync(string personNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personNo);
        await WaitForTeamReadSlotAsync(cancellationToken);
        var response = await GetSecureAsync<HikiotTeamPersonPayload>($"/team/v1/person/getByPersonNo?personNo={Uri.EscapeDataString(personNo)}", cancellationToken);
        if (response.Code == 150201) return null;
        EnsureSuccess(response);
        return response.Data is null ? null : ToTeamPerson(response.Data);
    }

    public async Task<HikiotTeamPersonCreateResult> CreateTeamPersonAsync(HikiotTeamPersonUpsert request, CancellationToken cancellationToken = default)
    {
        ValidateTeamPersonUpsert(request, false);
        var body = new { personName = request.Name.Trim(), departNo = request.DepartmentNo.Trim(), phone = request.Phone.Trim(), jobNumber = NullIfWhiteSpace(request.JobNumber), jobPosition = NullIfWhiteSpace(request.JobPosition), sex = request.Sex, idCard = NullIfWhiteSpace(request.IdCard) };
        var response = await PostSecureAsync<HikiotTeamPersonCreatedData>("/team/v1/person/add", body, cancellationToken);
        return new HikiotTeamPersonCreateResult(response.Code == 0, response.Code, response.Message, response.Data?.PersonNo, response.Detail);
    }

    public async Task<HikiotOperationResult> UpdateTeamPersonAsync(HikiotTeamPersonUpsert request, CancellationToken cancellationToken = default)
    {
        ValidateTeamPersonUpsert(request, true);
        // HIKIoT's update API intentionally does not accept phone, department or job number. Keep those immutable remotely.
        var body = new { personNo = request.PersonNo!.Trim(), personName = request.Name.Trim(), jobPosition = NullIfWhiteSpace(request.JobPosition), sex = request.Sex };
        return ToOperation(await PostSecureAsync<JsonElement>("/team/v1/person/update", body, cancellationToken));
    }

    public async Task<HikiotOperationResult> RemoveTeamPersonAsync(string personNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personNo);
        return ToOperation(await PostSecureAsync<JsonElement>("/team/v1/person/removeByNo", new { personNo = personNo.Trim() }, cancellationToken));
    }

    public async Task<IReadOnlyList<HikiotIdentification>> GetTeamIdentificationsAsync(string personNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personNo);
        await WaitForTeamReadSlotAsync(cancellationToken);
        var response = await GetSecureAsync<List<HikiotIdentificationPayload>>($"/team/v1/person/listIdentifications?personNo={Uri.EscapeDataString(personNo)}", cancellationToken);
        EnsureSuccess(response);
        return (response.Data ?? []).Where(x => x.Type is (int)HikiotIdentificationType.Card or (int)HikiotIdentificationType.FaceUrl)
            .Select(x => new HikiotIdentification(x.Id, (HikiotIdentificationType)x.Type, x.Content)).ToList();
    }

    public async Task<HikiotIdentificationResult> AddTeamIdentificationAsync(string personNo, HikiotIdentificationType type, string content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (type is not (HikiotIdentificationType.Card or HikiotIdentificationType.FaceUrl)) throw new ArgumentOutOfRangeException(nameof(type));
        var response = await PostSecureAsync<JsonElement>("/team/v1/person/addIdentification", new { personNo = personNo.Trim(), type = (int)type, content = content.Trim() }, cancellationToken);
        var id = TryReadLong(response.Data, "id");
        if (response.Code == 0 && id is null)
            id = (await GetTeamIdentificationsAsync(personNo, cancellationToken)).SingleOrDefault(x => x.Type == type && x.Content == content.Trim())?.Id;
        return new HikiotIdentificationResult(response.Code == 0, response.Code, response.Message, id, response.Detail);
    }

    public Task<HikiotIdentificationResult> AddTeamFaceAsync(string personNo, FaceAsset face, CancellationToken cancellationToken = default)
        => AddTeamIdentificationAsync(personNo, HikiotIdentificationType.FaceUrl, BuildFaceUrl(face.PublicToken), cancellationToken);

    public async Task<HikiotOperationResult> DeleteTeamIdentificationAsync(long identificationId, CancellationToken cancellationToken = default)
    {
        if (identificationId <= 0) throw new ArgumentOutOfRangeException(nameof(identificationId));
        return ToOperation(await PostSecureAsync<JsonElement>("/team/v1/person/deleteIdentification", new { id = identificationId }, cancellationToken));
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

    public async Task<IReadOnlyList<HikiotDiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
    {
        var groups = new List<HikiotGroupPageItem>();
        var page = 1;
        while (true)
        {
            var response = await GetSecureAsync<List<HikiotGroupPageItem>>($"/issue/v1/deviceGroup/page?page={page}&size=20&containsDefault=true", cancellationToken);
            EnsureSuccess(response);
            var data = response.Data ?? [];
            groups.AddRange(data);
            if (data.Count == 0 || groups.Count >= response.Count) break;
            page++;
        }

        var discovered = new List<HikiotDiscoveredDevice>();
        foreach (var group in groups)
        foreach (var serial in group.DeviceSerialList.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var capacityResponse = await GetSecureAsync<HikiotCapacityPayload>($"/issue/v1/device/capacityList?deviceSerial={Uri.EscapeDataString(serial)}", cancellationToken);
            EnsureSuccess(capacityResponse);
            var capacity = capacityResponse.Data ?? new HikiotCapacityPayload();
            discovered.Add(new HikiotDiscoveredDevice(group.DeviceGroupNo, group.DeviceGroupName, serial, new HikiotDeviceCapacity(
                capacity.SupportUserInfo,
                capacity.SupportCardInfo,
                capacity.SupportFace,
                capacity.SupportUserPassword,
                capacity.SupportPurePwdVerify,
                capacity.SupportRemoteControlDoor,
                capacity.SupportUserRightPlanTemplate)));
        }
        return discovered;
    }

    public async Task<HikiotOperationResult> EnsureAllDayTemplateAsync(string deviceSerial, CancellationToken cancellationToken = default)
    {
        var commands = HikiotAllDayTemplateFactory.Create(deviceSerial);
        var week = await PostSecureAsync<HikiotTraceData>("/device/direct/v1/timePlanAdd/userWeekPlan", commands.WeekPlan, cancellationToken);
        if (week.Code != 0 && !IsAlreadyExists(week)) return ToOperation(week);
        var template = await PostSecureAsync<HikiotTraceData>("/device/direct/v1/timePlanAdd/userPlanTemplate", commands.UserTemplate, cancellationToken);
        if (template.Code != 0 && !IsAlreadyExists(template)) return ToOperation(template);
        return new HikiotOperationResult(true, 0, "All-day access template is ready.", template.Data?.TraceId ?? week.Data?.TraceId);
    }

    public async Task<HikiotOperationResult> UpsertUserAsync(string deviceSerial, AccessPerson person, string? password, CancellationToken cancellationToken = default)
        => ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/userInfo/addOneRecord", HikiotUserCommandFactory.Create(deviceSerial, person, password), cancellationToken));

    public async Task<HikiotOperationResult> UpsertCardAsync(string deviceSerial, AccessPerson person, AccessCard card, CancellationToken cancellationToken = default)
    {
        var body = new { deviceSerial, payload = new { cardInfo = new { employeeNo = person.EmployeeNo, cardNo = card.CardNo, cardType = "normalCard" } } };
        return ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/cardInfo/addOneRecord", body, cancellationToken));
    }

    public async Task<HikiotOperationResult> UpsertFaceAsync(string deviceSerial, AccessPerson person, FaceAsset face, CancellationToken cancellationToken = default)
    {
        var body = new { deviceSerial, payload = new { faceInfo = new { employeeNo = person.EmployeeNo, faceURL = BuildFaceUrl(face.PublicToken), faceLibType = "blackFD", faceDbId = 1 } } };
        return ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/faceAccess/addOneRecord", body, cancellationToken));
    }

    public async Task<HikiotOperationResult> DeleteUserAsync(string deviceSerial, string employeeNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
        var body = new { deviceSerial, payload = new { userInfo = new { employeeNo } } };
        return ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/userInfo/deleteByKey", body, cancellationToken));
    }

    public async Task<HikiotOperationResult> DeleteCardAsync(string deviceSerial, string cardNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardNo);
        var body = new { deviceSerial, payload = new { cardInfo = new[] { new { cardNo } } } };
        return ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/cardInfo/batchDeleteByKey", body, cancellationToken));
    }

    public async Task<HikiotOperationResult> DeleteFaceAsync(string deviceSerial, string employeeNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
        var body = new { deviceSerial, payload = new { faceInfo = new { employeeNo, faceDbId = 1, faceLibType = "blackFD" } } };
        return ToOperation(await PostSecureAsync<HikiotTraceData>("/device/direct/v1/faceAccess/deleteByKey", body, cancellationToken));
    }

    public async Task<HikiotQrCodeResult> GenerateVisitorQrAsync(string deviceSerial, string cardNo, int expireMinutes, int maxOpenTimes, CancellationToken cancellationToken = default)
    {
        if (expireMinutes is < 5 or > 10080) throw new ArgumentOutOfRangeException(nameof(expireMinutes));
        if (maxOpenTimes is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(maxOpenTimes));
        var body = new { deviceSerial, payload = new { cardNo, expireMinutes, isValidTimes = true, maxOpenTimes } };
        var response = await PostSecureAsync<HikiotQrData>("/device/direct/v1/qrCodeInfo/genQrCode", body, cancellationToken);
        return new HikiotQrCodeResult(response.Code == 0, response.Code, response.Message, response.Data?.TraceId, response.Data?.QrCode,
            response.Data?.ExpireTime is long ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null, response.Detail);
    }

    public async Task<HikiotOperationResult> OpenDoorAsync(string resourceSerial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceSerial);
        var response = await GetSecureAsync<JsonElement>($"/issue/v1/device/openDoor?resourceSerial={Uri.EscapeDataString(resourceSerial)}", cancellationToken);
        return new HikiotOperationResult(response.Code == 0, response.Code, response.Message, null, response.Detail);
    }

    public async Task<HikiotPeopleSearchResult> SearchPeopleAsync(string deviceSerial, int page, int size, string? keyword, CancellationToken cancellationToken = default)
    {
        if (page < 1 || size is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(size));
        var body = new { deviceSerial, page, size, keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword };
        var response = await PostSecureAsync<List<HikiotUserSearchItem>>("/device/direct/v1/userInfo/search", body, cancellationToken);
        var people = (response.Data ?? []).Select(x => new HikiotRemotePerson(
            x.EmployeeNo, x.Name, x.UserType, x.PermanentValid,
            ParseDate(x.Valid?.BeginTime), ParseDate(x.Valid?.EndTime), x.OpenDoorTime, x.MaxOpenDoorTime, x.NumOfCard, x.NumOfFace)).ToList();
        return new HikiotPeopleSearchResult(response.Code == 0, response.Code, response.Message, response.Count, people, response.Detail);
    }

    public async Task<HikiotAuthorityConfigResult> SaveAuthorityConfigAsync(HikiotAuthorityConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConfigName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConfigDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PersonNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DeviceSerial);
        var path = string.IsNullOrWhiteSpace(request.ConfigId) ? "/issue/v1/authorityConfig/add" : "/issue/v1/authorityConfig/update";
        object requestBody = string.IsNullOrWhiteSpace(request.ConfigId)
            ? new
            {
                configName = request.ConfigName,
                configDesc = request.ConfigDescription,
                selectSubjectType = 1,
                timePlanId = request.TimePlanId,
                persons = new[] { new { type = 1, id = request.PersonNo } },
                devices = new[] { new { type = 1, id = request.DeviceSerial } }
            }
            : new
            {
                configId = request.ConfigId,
                configName = request.ConfigName,
                configDesc = request.ConfigDescription,
                selectSubjectType = 1,
                timePlanId = request.TimePlanId,
                persons = new[] { new { type = 1, id = request.PersonNo } },
                devices = new[] { new { type = 1, id = request.DeviceSerial } }
            };
        var response = await PostSecureAsync<JsonElement>(path, requestBody, cancellationToken);
        var configId = ReadString(response.Data, "configId") ?? ReadString(response.Data, "id") ?? ReadScalarString(response.Data) ?? request.ConfigId;
        return new HikiotAuthorityConfigResult(response.Code == 0, response.Code, response.Message, configId, response.Detail);
    }

    public async Task<HikiotOperationResult> DeleteAuthorityConfigAsync(string configId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configId);
        var response = await GetSecureAsync<JsonElement>($"/issue/v1/authorityConfig/delete?id={Uri.EscapeDataString(configId)}", cancellationToken);
        return ToOperation(response);
    }

    public async Task<IReadOnlyList<HikiotPersonDevice>> GetPersonDevicesAsync(string personNo, IReadOnlyCollection<string>? deviceSerials, int page, int size, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personNo);
        if (page < 1 || size is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(size));
        var path = $"/issue/v1/personDevice/page?page={page}&size={size}&personNos={Uri.EscapeDataString(personNo)}";
        if (deviceSerials is { Count: > 0 }) path += "&deviceSerials=" + Uri.EscapeDataString(string.Join(',', deviceSerials));
        var response = await GetSecureAsync<JsonElement>(path, cancellationToken);
        EnsureSuccess(response);
        var data = response.Data;
        if (data.ValueKind != JsonValueKind.Array) return [];
        var result = new List<HikiotPersonDevice>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadLong(item, "id");
            var remotePersonNo = ReadString(item, "personNo") ?? string.Empty;
            var serial = ReadString(item, "deviceSerial") ?? string.Empty;
            var states = new[]
                {
                    ReadCredentialState(item, "userVO", "person"),
                    ReadCredentialState(item, "faceVO", "face"),
                    ReadCredentialState(item, "fingerVO", "fingerprint"),
                    ReadCredentialState(item, "cardVO", "card"),
                    ReadCredentialState(item, "passwordVO", "password")
                }
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();
            var state = states.FirstOrDefault(x => x.Credential == "person") ?? states.FirstOrDefault();
            if (id is long personDeviceId && !string.IsNullOrWhiteSpace(remotePersonNo) && !string.IsNullOrWhiteSpace(serial))
                result.Add(new HikiotPersonDevice(personDeviceId, remotePersonNo, serial, state?.InfoStatus, state?.IsSupported, state?.IsSending, state?.LastFailedReason, states));
        }
        return result;
    }

    public async Task<HikiotIssueBatchResult> SelectIssueAsync(IReadOnlyCollection<long> personDeviceIds, CancellationToken cancellationToken = default)
    {
        if (personDeviceIds.Count == 0) return new HikiotIssueBatchResult(true, 0, "No pending authority changes.", null);
        if (personDeviceIds.Count > 10) throw new ArgumentOutOfRangeException(nameof(personDeviceIds), "HIKIoT selectIssue accepts at most ten person-device ids.");
        var response = await PostSecureAsync<JsonElement>("/issue/v1/issuedJob/selectIssue", new { personDeviceIds }, cancellationToken);
        var batchNo = ReadScalarString(response.Data) ?? ReadString(response.Data, "batchNo");
        return new HikiotIssueBatchResult(response.Code == 0, response.Code, response.Message, batchNo, response.Detail);
    }

    public async Task<IReadOnlyList<HikiotIssueBatchDetail>> GetIssueBatchDetailsAsync(string batchNo, int page, int size, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNo);
        var response = await PostSecureAsync<JsonElement>("/issue/v1/issuedJob/batchDetailPage", new { batchNo, page, size }, cancellationToken);
        EnsureSuccess(response);
        var data = response.Data;
        var array = data.ValueKind == JsonValueKind.Array ? data
            : data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Array ? nested
            : default;
        if (array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(item => new HikiotIssueBatchDetail(
            ReadLong(item, "personDeviceId") ?? ReadLong(item, "id"),
            ReadString(item, "status") ?? ReadString(item, "issueStatus"),
            ReadBoolean(item, "succeeded") ?? ReadBoolean(item, "success"),
            ReadString(item, "failureReason") ?? ReadString(item, "lastFailedReason") ?? ReadString(item, "msg"))).ToList();
    }

    private async Task ReadDepartmentsAsync(string? parentDepartmentNo, List<HikiotTeamDepartment> departments, HashSet<string?> visitedParents, CancellationToken cancellationToken)
    {
        if (!visitedParents.Add(parentDepartmentNo)) return;
        await WaitForTeamReadSlotAsync(cancellationToken);
        var path = string.IsNullOrWhiteSpace(parentDepartmentNo)
            ? "/team/v1/depart/getDeparts"
            : $"/team/v1/depart/getDeparts?departNo={Uri.EscapeDataString(parentDepartmentNo)}";
        var response = await GetSecureAsync<HikiotTeamDepartmentData>(path, cancellationToken);
        EnsureSuccess(response);
        foreach (var department in response.Data?.TeamDepartVOs ?? [])
        {
            if (string.IsNullOrWhiteSpace(department.DepartNo) || departments.Any(x => x.DepartmentNo == department.DepartNo)) continue;
            departments.Add(new HikiotTeamDepartment(department.DepartNo, department.DepartName, department.ParentId, department.IsLeaf, department.HavePerson, department.PersonNum, department.Path));
            if (!department.IsLeaf) await ReadDepartmentsAsync(department.DepartNo, departments, visitedParents, cancellationToken);
        }
    }

    private static List<HikiotTeamPersonPayload> ParseTeamPersonPage(JsonElement? data)
    {
        if (data is not JsonElement element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return [];
        var candidates = element.ValueKind == JsonValueKind.Array
            ? new[] { element }
            : new[] { "personInfoVOList", "list", "records", "data", "persons" }
                .Where(name => element.TryGetProperty(name, out _))
                .Select(name => element.GetProperty(name));
        foreach (var candidate in candidates)
        {
            if (candidate.ValueKind != JsonValueKind.Array) continue;
            var parsed = JsonSerializer.Deserialize<List<HikiotTeamPersonPayload>>(candidate.GetRawText(), JsonOptions);
            if (parsed is not null) return parsed;
        }
        return [];
    }

    private static HikiotTeamPerson ToTeamPerson(HikiotTeamPersonPayload person)
        => new(person.PersonNo, person.PersonName, person.Phone, person.TeamNo, person.DepartNo, person.JobNumber, person.JobPosition, person.Sex, person.IsOwner, person.PathName, person.IdCard);

    private static long? TryReadLong(JsonElement? element, string name)
    {
        if (element is not JsonElement value || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var id)) return null;
        return id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var numeric) ? numeric
            : id.ValueKind == JsonValueKind.String && long.TryParse(id.GetString(), out numeric) ? numeric
            : null;
    }

    private static string? ReadScalarString(JsonElement? element)
        => element is not JsonElement value ? null : value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };

    private static string? ReadString(JsonElement? element, string name)
    {
        if (element is not JsonElement value || value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        return property.ValueKind is JsonValueKind.String or JsonValueKind.Number ? property.ToString() : null;
    }

    private static string? ReadString(JsonElement element, string name) => ReadString((JsonElement?)element, name);
    private static long? ReadLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var number) ? number
            : property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out number) ? number : null;
    }
    private static bool? ReadBoolean(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when property.TryGetInt64(out var number) => number != 0,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            JsonValueKind.String when long.TryParse(property.GetString(), out var numeric) => numeric != 0,
            _ => null
        };
    }
    private static HikiotCredentialIssueState? ReadCredentialState(JsonElement item, string name, string credentialName)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var credential) || credential.ValueKind != JsonValueKind.Object) return null;
        var status = ReadLong(credential, "infoStatus") is long value ? (int?)value : null;
        return new HikiotCredentialIssueState(credentialName, status, ReadBoolean(credential, "isSupport"), ReadBoolean(credential, "isSending"), ReadString(credential, "lastFailedReason"));
    }

    private static void ValidateTeamPersonUpsert(HikiotTeamPersonUpsert request, bool isUpdate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        if (isUpdate) ArgumentException.ThrowIfNullOrWhiteSpace(request.PersonNo);
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.DepartmentNo);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Phone);
        }
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task WaitForTeamReadSlotAsync(CancellationToken cancellationToken)
    {
        await TeamReadGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var wait = NextTeamReadAtUtc - now;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);
            NextTeamReadAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(650);
        }
        finally { TeamReadGate.Release(); }
    }

    private async Task<HikiotEnvelope<T>> GetSecureAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        var tokens = await GetAuthorizedTokensAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
        AddTokens(request, tokens);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
    }

    private async Task<HikiotEnvelope<T>> PostSecureAsync<T>(string relativePath, object body, CancellationToken cancellationToken)
    {
        var tokens = await GetAuthorizedTokensAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = JsonContent.Create(body) };
        AddTokens(request, tokens);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
    }

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

    private async Task<HikiotEnvelope<T>> PostPublicAsync<T>(string relativePath, object body, string? appAccessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath) { Content = JsonContent.Create(body) };
        if (!string.IsNullOrWhiteSpace(appAccessToken)) request.Headers.Add("App-Access-Token", appAccessToken);
        return await SendAsync<T>(CreateClient(), request, cancellationToken);
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

    private async Task<HikiotConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = await db.HikiotConnections.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (connection is not null) return connection;
        connection = new HikiotConnection { Id = 1, NeedsReauthorization = true };
        db.HikiotConnections.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    private string BuildFaceUrl(string publicToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            throw new InvalidOperationException("Hikiot:PublicBaseUrl is not configured.");
        return $"{_options.PublicBaseUrl.TrimEnd('/')}/public/faces/{Uri.EscapeDataString(publicToken)}";
    }

    private void ValidateSetup()
    {
        if (string.IsNullOrWhiteSpace(_options.AppKey) || string.IsNullOrWhiteSpace(_options.AppSecret) || string.IsNullOrWhiteSpace(_options.RedirectUri))
            throw new InvalidOperationException("Hikiot AppKey, AppSecret, and RedirectUri must be configured.");
    }

    private static void EnsureSuccess<T>(HikiotEnvelope<T> response)
    {
        if (response.Code != 0) throw new InvalidOperationException($"HIKIoT request failed: {response.Code} {response.Message}");
    }

    private static HikiotOperationResult ToOperation<T>(HikiotEnvelope<T> response)
        => new(response.Code == 0, response.Code, response.Message, response.Data is HikiotTraceData trace ? trace.TraceId : null, response.Detail);

    private static bool IsAlreadyExists<T>(HikiotEnvelope<T> response)
    {
        var text = $"{response.Message} {response.Detail}";
        return text.Contains("already exist", StringComparison.OrdinalIgnoreCase)
               || text.Contains("already exists", StringComparison.OrdinalIgnoreCase)
               || text.Contains("已存在", StringComparison.Ordinal)
               || text.Contains("重复", StringComparison.Ordinal);
    }

    private static DateTime? ParseDate(string? value) => DateTime.TryParse(value, out var parsed) ? parsed : null;
}
