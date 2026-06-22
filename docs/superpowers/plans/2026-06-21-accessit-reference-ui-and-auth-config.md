# AccessIt Reference UI and Auth Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align the AccessIt user interface with the simple AuditIt desktop shell and remove the unnecessary configurable DingTalk browser redirect URI.

**Architecture:** Keep DingTalk browser OAuth entirely client-driven: build the redirect URI from the active origin and `/login`, exactly as AuditIt does. Keep the HIKIoT OAuth callback server-side because it receives the authorization code used to persist the HIKIoT team token. Replace the decorative AccessIt shell with AuditIt's dark collapsible navigation, white header, grey page background and plain white content area.

**Tech Stack:** Vue 3, TypeScript, Ant Design Vue 4, Vitest, ASP.NET Core configuration.

---

### Task 1: Remove the configurable DingTalk browser redirect URI

**Files:**
- Create: `AccessIt.Web/src/utils/dingtalkAuth.ts`
- Create: `AccessIt.Web/src/utils/dingtalkAuth.test.ts`
- Modify: `AccessIt.Web/src/pages/LoginPage.vue`
- Modify: `AccessIt.Web/.env.example`
- Modify: `AccessIt.Api/Configuration/ApplicationOptions.cs`
- Modify: `AccessIt.Api/appsettings.Development.example.json`

- [ ] **Step 1: Write the failing frontend redirect-uri test**

```ts
import { describe, expect, it } from 'vitest'
import { getDingTalkBrowserRedirectUri } from './dingtalkAuth'

describe('getDingTalkBrowserRedirectUri', () => {
  it('uses the current site login route instead of an environment override', () => {
    expect(getDingTalkBrowserRedirectUri('https://door.example.com')).toBe('https://door.example.com/login')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- --run src/utils/dingtalkAuth.test.ts`

Expected: a module-not-found failure for `./dingtalkAuth`.

- [ ] **Step 3: Implement the dynamic URI helper and use it in login**

```ts
export function getDingTalkBrowserRedirectUri(origin: string): string {
  return `${origin.replace(/\/$/, '')}/login`
}
```

Replace the environment-based expression in `LoginPage.vue` with:

```ts
const redirect = encodeURIComponent(getDingTalkBrowserRedirectUri(window.location.origin))
```

Delete `VITE_DINGTALK_REDIRECT_URI` from `.env.example`, `WebRedirectUri` from `DingTalkOptions`, and the corresponding development settings entry.

- [ ] **Step 4: Run the focused test and production type/build check**

Run: `npm test -- --run src/utils/dingtalkAuth.test.ts`

Expected: 1 passing test.

Run: `npm run build`

Expected: exit code 0.

### Task 2: Match the AuditIt desktop shell and simplify the login page

**Files:**
- Modify: `AccessIt.Web/src/layouts/AppShell.vue`
- Modify: `AccessIt.Web/src/style.css`
- Modify: `AccessIt.Web/src/pages/LoginPage.vue`
- Modify: `AccessIt.Web/src/pages/DashboardPage.vue`

- [ ] **Step 1: Write the failing layout contract test**

Create `AccessIt.Web/src/utils/layout.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { appShellStyle } from './layout'

describe('appShellStyle', () => {
  it('uses AuditIt-compatible neutral desktop surfaces', () => {
    expect(appShellStyle.pageBackground).toBe('#f0f2f5')
    expect(appShellStyle.contentBackground).toBe('#fff')
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm test -- --run src/utils/layout.test.ts`

Expected: a module-not-found failure for `./layout`.

- [ ] **Step 3: Implement neutral shell tokens and apply them**

Create `AccessIt.Web/src/utils/layout.ts`:

```ts
export const appShellStyle = {
  pageBackground: '#f0f2f5',
  contentBackground: '#fff'
} as const
```

Update `AppShell.vue` so its sider is collapsible, the brand is text-only “开一个门”, the header has only the current user and logout, and `a-layout-content` wraps `RouterView` in a white canvas with 24px padding and a 16px outer margin. Update `style.css` to use `#f0f2f5` for the document background. Remove the header shadow and the subtitle/role decoration.

Update `LoginPage.vue` to follow the AuditIt login surface: grey background, standard titled Ant Design card, no emoji/logo, no gradient, and no custom card shadow.

Update `DashboardPage.vue` to use one ordinary card containing status descriptions and the existing setup steps. Keep all current data loading and status values.

- [ ] **Step 4: Run UI tests and build**

Run: `npm test -- --run`

Expected: all frontend tests pass.

Run: `npm run build`

Expected: exit code 0.

### Task 3: Verify server configuration and the HIKIoT callback boundary

**Files:**
- Modify: `AccessIt.Api.Tests/HikiotGatewayTokenTests.cs`
- Modify: `AccessIt.Api/appsettings.json`
- Modify: `AccessIt.Api/appsettings.Development.example.json`

- [ ] **Step 1: Add an assertion that HIKIoT authorization still uses its configured callback**

Extend the existing token test to assert that the URL from `BeginAuthorizationAsync` contains:

```csharp
Assert.Contains("redirectUrl=https%3A%2F%2Fapi.example.com%2Fapi%2Fhikiot%2Fconnection%2Fcallback", authorizationUrl);
```

- [ ] **Step 2: Run the test to verify the callback assertion passes**

Run: `dotnet test .\\AccessIt.Api.Tests\\AccessIt.Api.Tests.csproj --no-restore --filter FullyQualifiedName~HikiotGatewayTokenTests`

Expected: 1 passing test.

- [ ] **Step 3: Make configuration examples explicit**

Keep only the HIKIoT `RedirectUri` in server configuration. Label `PublicBaseUrl` as required only by face-image download and public visitor QR links. Keep the production `appsettings.json` values blank/placeholders, and keep actual secrets outside version control.

- [ ] **Step 4: Run the full verification suite**

Run: `dotnet test .\\AccessIt.slnx --no-restore`

Expected: all backend tests pass.

Run: `npm test -- --run`

Expected: all frontend tests pass.

Run: `npm run build`

Expected: exit code 0.

## Self-review

- Spec coverage: Task 1 removes the unnecessary DingTalk configuration; Task 2 covers the requested AuditIt-style visual alignment; Task 3 preserves and documents the required HIKIoT authorization callback.
- Placeholder scan: no TODO or unspecified implementation actions remain.
- Type consistency: the helper names, test names and configuration property names match the source files named in each task.
