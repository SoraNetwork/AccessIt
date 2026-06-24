# AccessIt 架构与运作文档（重构梳理版）

> 本文档基于对全部源码的逐文件阅读整理而成，目标是把"这个项目到底怎么跑、用了哪些 HIKIoT API、流程怎么走"讲清楚，供重构决策使用。

---

## 1. 项目定位

AccessIt 是一套**门禁管理系统**的中后台，对外对接两个第三方平台：

| 平台 | 角色 | 用途 |
|------|------|------|
| **HIKIoT**（海康威视物联网开放平台） | 门禁设备与团队/权限中枢 | 真正的"开门"能力来源：人员、卡、人脸、设备、权限下发、远程开门、访客二维码 |
| **钉钉（DingTalk）** | 身份与组织目录 | 登录身份认证（SSO）、企业通讯录同步、工作通知推送 |

业务上把"人"分成两类，走**完全不同的下发路径**：

- **员工 Employee**：通过 HIKIoT 的 **团队（Team）API + 权限配置（authorityConfig）+ 下发任务（issuedJob）** 这条"标准权限"链路管理。
- **访客 Visitor**：通过 HIKIoT 的 **设备直连（device direct）API** 直接往单台设备写用户/卡/人脸，并生成临时二维码。

> ⚠️ 这套双路径设计是整个项目最核心、也最容易出问题的点。详见第 5、6 节。

---

## 2. 技术栈与拓扑

```
┌─────────────────────────┐        ┌──────────────────────────┐
│  AccessIt.Web (Vue3)    │  JWT   │  AccessIt.Api (.NET 8)   │
│  AntDesign Vue + Vite   │◀──────▶│  Minimal API / Controllers│
│  Pinia + Vue Router     │  HTTP  │  EF Core (SQLite)        │
└─────────────────────────┘        │  BackgroundServices ×4   │
                                   └────────────┬─────────────┘
                                                │ HTTPS
                          ┌─────────────────────┼─────────────────────┐
                          ▼                     ▼                     ▼
                 open-api.hikiot.com     api.dingtalk.com        本地文件
                 (设备/团队/权限)          (身份/通讯录)           (人脸图片)
```

- **数据库**：SQLite（`accessit.db`），启动时 `Database.MigrateAsync()` 自动迁移。
- **密钥保护**：`IDataProtector`（`data-protection-keys/` 目录）加密 HIKIoT/DingTalk token，`SecretProtector` 封装。
- **后台服务**（`Program.cs` 注册 4 个 `IHostedService`）：
  1. `IssuanceJobWorker`——轮询访客直连下发任务（100ms/3s）。
  2. `HikiotIssueReconcileWorker`——轮询员工标准权限下发批次（20s）。
  3. `DingTalkDirectorySyncWorker`——定时同步钉钉通讯录（默认 24h）。
  4. `VisitorExpiryWorker`——访客过期清理（15min）。

---

## 3. 认证与配置

### 3.1 配置项（`appsettings.json`）

```jsonc
"Hikiot": {
  "AppKey": "",        // HIKIoT 应用 Key
  "AppSecret": "",     // HIKIoT 应用 Secret
  "ApiBaseUrl": "https://open-api.hikiot.com",
  "RedirectUri": "",   // OAuth 回调，必须服务端可达：https://<host>/api/hikiot/connection/callback
  "PublicBaseUrl": ""  // 暴露给 HIKIoT 回拉人脸图/访客二维码的公网基地址
},
"DingTalk": {
  "AppKey": "", "AppSecret": "", "AgentId": null,
  "BootstrapAdminNames": [],   // 首次登录自动成为 SuperAdmin 的钉钉用户名
  "DirectorySyncHours": 24
},
"Jwt": { "Key": "...", "ExpiresHours": 12 }
```

### 3.2 登录流程（钉钉 SSO）

前端 `LoginPage.vue` 走钉钉 OAuth（`getDingTalkBrowserRedirectUri` 用 `origin + /login`），拿到 `code` 后二选一调后端：

- `POST /api/auth/dingtalk/web`（浏览器扫码）→ `GetWebProfileAsync`：用 code 换 userAccessToken → 拉 `/contact/users/me`。
- `POST /api/auth/dingtalk/in-app`（钉钉客户端内）→ `GetInAppProfileAsync`：用 code + 企业 token 换 userId → 再拉详情。

后端 `IdentityService.SignInAsync`：按 UnionId → UserId 匹配/建 `ApplicationUser`，若姓名命中 `BootstrapAdminNames` 则授 `SuperAdmin`。**新用户默认 Role = None**，会被前端路由强制跳到 `/pending-access` 等待管理员授权。

### 3.3 角色体系（`ApplicationRole`）

`None` / `SuperAdmin` / `AccessAdmin` / `Auditor`，控制器用 `[Authorize(Roles=...)]` 逐接口管控。SuperAdmin 才能配 HIKIoT 连接、管用户。

---

## 4. HIKIoT 连接与授权（前置必做）

这是**所有门禁功能的总闸**，没通过这关一切 API 都会抛 `HIKIoT requires user authorization`。

### 4.1 两层 Token 模型

HIKIoT 是**双层 OAuth**：

1. **App Token（应用级）**——`HikiotConnection.ProtectedAppAccessToken`
   - 获取：`POST /auth/exchangeAppToken` `{appKey, appSecret}`
   - 刷新：`POST /auth/refreshAppToken` `{appAccessToken, refreshAppToken}`
   - 有效期：小时级。`GetAppTokenAsync` 在剩余 <5 分钟时刷新，带 `TokenGate` 信号量防并发。

2. **User Token（团队级）**——`ProtectedUserAccessToken`
   - 获取：用户走 OAuth 授权 → `GET /auth/third/code2Token?authCode=`
   - 刷新：`POST /auth/third/refreshUserAccessToken`
   - 有效期：天级。剩余 <1 天时刷新。失败则置 `NeedsReauthorization=true`，必须人工重授权。

所有业务请求头同时带 `App-Access-Token` + `User-Access-Token`（`AddTokens`）。

### 4.2 授权流程（`HikiotConnectionController`，仅 SuperAdmin）

```
前端点"授权"
  → POST /api/hikiot/connection/authorize
     → BeginAuthorizationAsync: 生成 state 存库（10min 过期），返回
       https://open.hikiot.com/oauth/thirdpart?state=&appKey=&redirectUrl=
用户在 HIKIoT 页面同意
  → HIKIoT 回跳 GET /api/hikiot/connection/callback?state=&authCode=
     → CompleteAuthorizationAsync: 校验 state → code2Token → 写 connection 表
     → 返回纯文本"授权成功"
```

授权后还要 `PUT /api/hikiot/connection/default-department` 设默认部门（`SetDefaultDepartmentAsync`），因为新建团队人员必须指定 `departNo`。

### 4.3 部门树（限流）

`GetTeamDepartmentsAsync` 递归拉 `/team/v1/depart/getDeparts`。**注意 `WaitForTeamReadSlotAsync` 强制每次团队读请求间隔 650ms**（HIKIoT 限流），这是个全局静态节流阀。

---

## 5. HIKIoT API 全量清单

下表按"功能域"整理 `HikiotGateway` 用到的全部 HIKIoT 接口。**这是重构时最关键的对照表。**

### 5.1 鉴权域

| 方法 | HIKIoT API | 说明 |
|------|-----------|------|
| GetAppTokenAsync | `POST /auth/exchangeAppToken` | appKey/secret 换应用 token |
| GetAppTokenAsync | `POST /auth/refreshAppToken` | 刷新应用 token |
| BeginAuthorizationAsync | `open.hikiot.com/oauth/thirdpart`（页面跳转） | 发起用户授权 |
| CompleteAuthorizationAsync | `GET /auth/third/code2Token` | authCode 换用户 token |
| GetAuthorizedTokensAsync | `POST /auth/third/refreshUserAccessToken` | 刷新用户 token |

### 5.2 团队人员域（Team）——员工身份与凭证

| 方法 | HIKIoT API | 说明 |
|------|-----------|------|
| GetTeamPeopleAsync | `GET /team/v1/person/page?departNo=&page=&size=` | 分页查部门下人员 |
| GetTeamPersonAsync | `GET /team/v1/person/getByPersonNo` | 单人详情（补全 page 缺字段） |
| CreateTeamPersonAsync | `POST /team/v1/person/add` | 新建团队人员，返回 personNo |
| UpdateTeamPersonAsync | `POST /team/v1/person/update` | 更新（**不接受 phone/depart/jobNumber**） |
| RemoveTeamPersonAsync | `POST /team/v1/person/removeByNo` | 删除团队人员 |
| GetTeamIdentificationsAsync | `GET /team/v1/person/listIdentifications` | 列卡/人脸凭证 |
| AddTeamIdentificationAsync | `POST /team/v1/person/addIdentification` | 加卡(type=1)/人脸URL(type=3) |
| AddTeamFaceAsync | `POST /team/v1/person/faceDetect` → addIdentification | 人脸先检测(score≥80)再加 |
| DeleteTeamIdentificationAsync | `POST /team/v1/person/deleteIdentification` | 删凭证 |
| FaceDetectAsync | `POST /team/v1/person/faceDetect` | 人脸质量评分 |

> 人脸 URL 由 `BuildFaceUrl` 生成：`{PublicBaseUrl}/public/faces/{publicToken}`，HIKIoT 会回拉这张图，所以 **PublicBaseUrl 必须公网可达**。

### 5.3 设备域（Issue）——发现与能力

| 方法 | HIKIoT API | 说明 |
|------|-----------|------|
| DiscoverDevicesAsync | `GET /issue/v1/deviceGroup/page?page=&size=&containsDefault=true` | 分页拉设备组 |
| DiscoverDevicesAsync | `GET /issue/v1/device/capacityList?deviceSerial=` | 每台设备的能力位（是否支持人脸/卡/密码等） |
| OpenDoorAsync | `GET /issue/v1/device/openDoor?resourceSerial=` | 远程开门 |

### 5.4 权限配置域（authorityConfig）——**员工下发核心**

| 方法 | HIKIoT API | 说明 |
|------|-----------|------|
| SaveAuthorityConfigAsync | `POST /issue/v1/authorityConfig/add` | 新建权限配置（人↔设备↔时间计划） |
| SaveAuthorityConfigAsync | `POST /issue/v1/authorityConfig/update` | 更新权限配置（带 configId） |
| DeleteAuthorityConfigAsync | `GET /issue/v1/authorityConfig/delete?id=` | 删权限配置（撤权） |
| GetPersonDevicesAsync | `GET /issue/v1/personDevice/page?personNos=&deviceSerials=` | 查人-设备下发状态 |
| SelectIssueAsync | `POST /issue/v1/issuedJob/selectIssue` | **触发下发**（≤10 个 personDeviceId） |
| GetIssueBatchDetailsAsync | `POST /issue/v1/issuedJob/batchDetailPage` | 查批次明细 |

### 5.5 设备直连域（device direct）——**访客/密码下发核心**

| 方法 | HIKIoT API | 说明 |
|------|-----------|------|
| EnsureAllDayTemplateAsync | `POST /device/direct/v1/timePlanAdd/userWeekPlan` | 建"全天通行"周计划 |
| EnsureAllDayTemplateAsync | `POST /device/direct/v1/timePlanAdd/userPlanTemplate` | 建计划模板（doorPlanTemplateId=8） |
| UpsertUserAsync | `POST /device/direct/v1/userInfo/addOneRecord` | 写/更新设备用户（含密码、有效期、门权限） |
| UpsertCardAsync | `POST /device/direct/v1/cardInfo/addOneRecord` | 写设备卡 |
| UpsertFaceAsync | `POST /device/direct/v1/faceAccess/addOneRecord` | 写设备人脸（faceLibType=blackFD） |
| DeleteUserAsync | `POST /device/direct/v1/userInfo/deleteByKey` | 删设备用户 |
| DeleteCardAsync | `POST /device/direct/v1/cardInfo/batchDeleteByKey` | 删设备卡 |
| DeleteFaceAsync | `POST /device/direct/v1/faceAccess/deleteByKey` | 删设备人脸 |
| GenerateVisitorQrAsync | `POST /device/direct/v1/qrCodeInfo/genQrCode` | 生成访客动态二维码 |
| SearchPeopleAsync | `POST /device/direct/v1/userInfo/search` | 搜设备上的人员（同步对账用） |

### 5.6 通用响应约定

所有 HIKIoT 接口返回统一信封 `HikiotEnvelope<T>`：`{code, msg, data, detail, count}`，**code==0 才算成功**。错误分类见 `HikiotErrorClassifier`：

- **可重试**（160099/160200/10002 等）→ 任务自动重试（最多 4 次）。
- **终态**（160103 不存在、160104 卡冲突、160108 人脸分低…）→ 直接失败，需人工介入。
- **120524 "无需下发"** → 当成功处理。

---

## 6. 核心业务流程详解

### 6.1 员工全生命周期（标准权限链路）

这是最复杂的一条链，涉及 **团队 API + 权限配置 + 下发确认**三段。

#### A. 新建/发布员工（`PersonService.CreateEmployeeAsync` → `HikiotTeamPeopleService.PublishAsync`）

```
CreateEmployee
 ├─ 分配 EmployeeNo（PersonNumberGenerator，E+序号）
 ├─ teamPeople.PublishAsync(personId)
 │   ├─ 1. 团队人员
 │   │   ├─ 无 personNo → CreateTeamPerson（需默认部门 + 11位手机号校验）
 │   │   └─ 有 personNo → UpdateTeamPerson
 │   ├─ 2. 团队凭证（卡/人脸）
 │   │   ├─ 卡：先比对旧 identificationId，变了先 DeleteTeamIdentification 再 addIdentification（≤4张物理卡）
 │   │   └─ 人脸：删光旧 FaceUrl → AddTeamFace（faceDetect score≥80）
 │   ├─ 3. 给所有 SupportsUserInfo 设备建 DeviceGrant
 │   └─ 4. authorityIssuance.PublishEmployeeAsync ←── 关键
 └─ 审计
```

`PublishEmployeeAsync`（`StandardAuthorityIssuanceService`）做权限下发：

```
对每台目标设备:
  SaveAuthorityConfig(add/update)  → 拿 configId 存 DeviceGrant.HikiotAuthorityConfigId
等 2 秒让 HIKIoT 建 person-device 映射
ReadPersonDevicesAsync → 拿 personDeviceId 存 DeviceGrant
对未完成的 personDeviceId（每批≤10）:
  SelectIssue → 触发下发，拿 batchNo 存 HikiotIssueBatch + DeviceGrant.HikiotIssueBatchNo
轮询 8 次（2-5秒间隔）确认状态:
  InfoStatus==2(成功) / 3(删除) / 有 lastFailedReason(失败)
最终批次标 Succeeded/Pending/Failed
```

#### B. 撤权（`RevokeEmployeeAsync`）

```
对每个 active grant:
  DeleteAuthorityConfig(configId) → grant.IsActive=false
SubmitAndConfirmAsync(revoking=true):
  SelectIssue 触发删除下发
  确认（设备不再返回该 personDevice = 撤权成功）
```

#### C. 删员工（`PersonService.DeleteAsync`，员工分支）

```
authorityIssuance.RevokeEmployeeAsync  ← 先撤标准权限
对每台设备逐个 DeleteFace/DeleteCard/DeleteUser  ← 再清设备直连残留
```

#### D. 异步兜底（`HikiotIssueReconcileWorker`，20s 一次）

`ReconcilePendingAsync` 把状态仍为 `Submitted/Pending` 的 `HikiotIssueBatch` 重新读 `personDevice` 状态，收敛到 `Succeeded/Failed`。**解决了"下发是异步的，但发布接口要等确认"的体验问题**。

### 6.2 访客全生命周期（设备直连链路）

#### A. 新建访客（`CreateVisitorAsync` → `IssuanceJobService.QueueUpsertAsync`）

访客**不进 HIKIoT 团队**，直接走设备直连任务队列。

```
CreateVisitor(分配 V+序号 EmployeeNo，带有效期/开门次数)
 ├─ AssignDevices（指定具体设备，非全员）
 └─ jobs.QueueUpsertAsync
      └─ IssuanceWorkflowBuilder.BuildUpsertSteps 生成步骤序列:
           [EnsureAllDayTemplate?] → UpsertUser → [UpsertCard] → [UpsertFace]
```

任务由 `IssuanceJobService` 编排：

- 按 `ParentJobId + Sequence` 保证**前序步骤全成功才执行后续**；前序失败则后续全部 Cancelled。
- 单步失败若可重试，最多 4 次自动重试，否则 Failed。
- `EnsureTemplateAsync` 成功后**硬编码 `device.AllDayTemplateId = 8`**（⚠️ 见第 8 节风险）。

#### B. 访客二维码（`VisitorQrService.IssueAsync`）

这条链路是同步的、要求严格：

```
校验 visitor 授权该设备 + 设备支持 UserInfo/CardInfo
校验有效期（距过期 >5 分钟）
分配虚拟卡（CardNo = Q{sequence:X10}）
EnsureAllDayTemplate（若需要，含 160103 重试逻辑）
UpsertUser → UpsertCard → WaitForVirtualCardAsync(轮询8次确认设备已落卡)
GenerateVisitorQrAsync → 生成二维码
存 VisitorQrShare(opaque token)，返回公开分享 URL
```

> `WaitForVirtualCardAsync` 的存在说明：设备直连 API **返回 traceId 不代表硬件已写入**，有短暂窗口期，QR 接口会因此报错。

#### C. 过期清理（`VisitorExpiryWorker`，15min 一次）

扫过期的非永久访客 → 标 Expired + 吊销 QR + `QueueDeleteAsync`（删卡/脸/用户）。

### 6.3 设备同步对账（`DeviceSyncService`）

```
POST /api/devices/{id}/sync
 └─ SearchPeopleAsync 分页拉设备现有人员
    逐个对比本地 AccessPeople:
      本地无 → 记 "remoteRecord" 冲突
      字段不一致(name/permanentValid/enableEndTime) → 记冲突
冲突解决 ResolveAsync:
  KeepLocal → 员工走 PublishAsync / 访客走 QueueUpsert（推本地覆盖设备）
  KeepDevice → 直接改本地值
```

---

## 7. 数据模型要点（领域层）

```
AccessPerson (核心)
 ├─ EmployeeNo         设备直连用（E/V + 序号）
 ├─ HikiotPersonNo     团队人员用（两套号 deliberately 分离）
 ├─ Kind: Employee/Visitor
 ├─ PermanentValid / EnableBeginTime/EndTime / MaxOpenDoorTime
 ├─ DeviceGrant[]      人-设备授权（存 configId/personDeviceId/状态）
 ├─ AccessCard[]       CardNo + IsVirtual + HikiotIdentificationId
 ├─ FaceAsset[]        人脸图（PublicToken + HikiotIdentificationId）
 └─ DevicePassword     设备密码（加密存）

AccessDevice
 ├─ DeviceSerial + 能力位 Supports*
 └─ HasAllDayTemplate / AllDayTemplateId  ← 本地缓存

HikiotConnection (单行 Id=1)
 └─ 所有 token（加密）+ NeedsReauthorization 总闸

HikiotIssueBatch / IssuanceJob / SyncRun+SyncConflict / VisitorQrShare / AuditEvent
```

---

## 8. 重构关注点（风险与坏味道）

阅读源码时发现的问题，按严重度排序：

### 🔴 高风险

1. **`AllDayTemplateId` 硬编码 = 8**
   `IssuanceJobService.EnsureTemplateAsync` 和 `VisitorQrService`、`PersonService.DistributeEmployeePasswordAsync` 里，模板创建成功后都写死 `device.AllDayTemplateId = 8`。注释（`DeviceGrant.cs`）明确说"必须用 EnsureAllDayTemplateAsync 返回的 ID，绝不能硬编码"，但代码恰恰硬编码了。多设备/多模板场景会错配门权限。

2. **错误处理不彻底**
   `IsAlreadyUpToDate` / `IsCapacityError` / `IsUnsupportedCredential` / `IsFaceScoreFailure` / `IsPasswordError` 这些分类方法在 `HikiotErrorClassifier` 里定义了，但**业务层几乎没用到**（只有 `IsRetryable` 被调用）。终态错误没有差异化提示，运维难定位。

3. **`SearchPeopleAsync` 的 `keyword` 行为不一致**
   `DeviceSyncService` 拉全量用 `keyword=null` 分页，但 `WaitForVirtualCardAsync` 用 `keyword=employeeNo` 单查。HIKIoT 该接口是否真支持精确 keyword 过滤存疑，影响虚拟卡等待的可靠性。

### 🟡 中风险

4. **双路径认知负担**
   员工走标准权限、访客走设备直连，但 `PersonService.DeleteAsync` 删员工时又混用了设备直连 `DeleteFace/DeleteCard/DeleteUser`。`IssuanceJobService` 里还保留了对员工 job 的兼容判断（直接 Cancel）。边界混乱。

5. **`UpdateTeamPersonAsync` 字段不可变**
   团队更新接口不接受 phone/depart/jobNumber（代码注释说明），但前端/UI 层是否约束了这点不明确，用户改了手机号可能"看起来成功实际没下发"。

6. **节流阀是静态全局**
   `HikiotGateway` 里 `TokenGate`/`TeamReadGate`/`NextTeamReadAtUtc` 都是 `static`，多实例部署（横向扩展）时无法共享节流状态，可能触发 HIKIoT 限流。

7. **确认轮询同步阻塞**
   `SubmitAndConfirmAsync` 在请求线程里 `Task.Delay` 轮询 8 次（最长约 30s），员工发布接口可能超时。虽有 `HikiotIssueReconcileWorker` 兜底，但首次发布体验差。

### 🟢 低风险 / 改进项

8. **`PublicBaseUrl` 强依赖公网**：人脸 URL 和访客二维码分享都靠它，本地开发无法完整测试。
9. **审计详情用 JSON 序列化匿名对象**：无 schema，查询困难。
10. **SQLite 单文件**：并发写入瓶颈，生产应换 PostgreSQL。
11. **无重试退避策略**：重试固定间隔，非指数退避。
12. **`HikiotConnection` 单行设计（Id=1）**：天然不支持多租户/多团队。

---

## 9. 重构建议优先级

| 优先级 | 动作 | 理由 |
|--------|------|------|
| P0 | 修掉 `AllDayTemplateId=8` 硬编码，改读 API 返回值 | 当前是潜在的门权限错配 bug |
| P0 | 把 `HikiotErrorClassifier` 的分类真正接到业务提示 | 让运维能区分"卡冲突"vs"设备离线" |
| P1 | 统一双路径或明确边界（员工只用标准权限，设备直连只给访客/密码） | 降低认知负担，减少 DeleteAsync 的混用 |
| P1 | 把 `SubmitAndConfirmAsync` 改全异步（发布即返回，靠 ReconcileWorker 收敛） | 解决超时 |
| P2 | 节流状态外置（Redis）支持多实例 | 横向扩展前提 |
| P2 | 数据库迁 PostgreSQL | 生产可用性 |
| P3 | 补 HIKIoT 接口契约测试（mock 网关） | 重构安全网 |

---

## 附：API 端点速查

| 方法 | 路径 | 角色 | 用途 |
|------|------|------|------|
| POST | /api/auth/dingtalk/web|in-app | 匿名 | 钉钉登录 |
| GET | /api/auth/me | 任意已登录 | 当前用户 |
| GET | /api/hikiot/connection | SuperAdmin | 连接状态 |
| GET | /api/hikiot/departments | SuperAdmin | 部门树 |
| PUT | /api/hikiot/connection/default-department | SuperAdmin | 设默认部门 |
| POST | /api/hikiot/connection/authorize | SuperAdmin | 发起授权 |
| GET | /api/hikiot/connection/callback | 匿名 | OAuth 回调 |
| GET | /api/devices | 三角色 | 设备列表 |
| POST | /api/devices/discover | 管理员 | 发现设备 |
| POST | /api/devices/{id}/open | 管理员 | 远程开门 |
| POST | /api/devices/{id}/sync | 管理员 | 设备对账 |
| GET | /api/people | 三角色 | 人员列表 |
| POST | /api/people/employees | 管理员 | 建员工 |
| POST | /api/people/visitors | 管理员 | 建访客 |
| POST | /api/people/sync-sources | 管理员 | 同步 HIKIoT+钉钉人员 |
| POST | /api/people/{id}/hikiot/publish | 管理员 | 发布员工到团队 |
| DELETE | /api/people/{id}/hikiot | 管理员 | 移出团队 |
| POST|PUT | /api/people/{id}/cards | 管理员 | 卡管理 |
| POST | /api/people/{id}/face | 管理员 | 上传人脸 |
| PUT | /api/people/{id}/password | 管理员 | 设密码 |
| GET | /api/jobs | 三角色 | 下发任务列表 |
| POST | /api/jobs/{id}/retry | 管理员 | 重试任务 |
| POST | /api/visitor-qr | 管理员 | 生成访客二维码 |
| GET | /public/visitor-qr/{token} | 匿名 | 公开二维码页 |
| GET | /public/faces/{token} | 匿名 | HIKIoT 回拉人脸图 |
