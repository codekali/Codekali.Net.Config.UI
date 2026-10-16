# Codekali.Net.Config.UI — UI Tests

End-to-end UI tests using **Playwright** + **xUnit**. Every test spins up a real Chromium browser and drives the Config UI exactly as a developer would, verifying that the full middleware pipeline — API, embedded assets, Monaco, tree rendering — works correctly after any redesign.

---

## Prerequisites

- .NET 8 SDK
- Node.js (for `playwright install` — only needed once)
- PowerShell 7+ (`pwsh`) — only for the install script

---

## One-time setup

```bash
# 1. Build the test project
dotnet build

# 2. Install Playwright's Chromium browser
pwsh playwright.config.ps1
# OR directly:
dotnet build && dotnet run --project . -- install chromium
```

---

## Running tests

### Against a running instance (recommended for CI)

Point the tests at your already-running sample app:

```bash
CONFIGUI_TEST_URL=http://localhost:5100 dotnet test
```

### Letting the tests start the host themselves (local dev)

The fixture will `dotnet run` the sample app automatically if `CONFIGUI_TEST_URL` is not set. The sample app must be in `samples/SampleWebApp/` relative to the solution root.

```bash
dotnet test
```

### With an access token

```bash
CONFIGUI_TEST_URL=http://localhost:5100 \
CONFIGUI_ACCESS_TOKEN=your-secret-token \
dotnet test
```

---

## Watching tests run (debug mode)

In `ConfigUITestFixture.cs`, change:

```csharp
Headless = true,
```

to:

```csharp
Headless = false,
SlowMo = 300,
```

Re-run and Chromium opens visually.

---

## Test structure

| File | What it covers |
|---|---|
| `Fixtures/ConfigUITestFixture.cs` | Host lifecycle, Playwright browser + context, temp config directory seeding |
| `Pages/ConfigUIPage.cs` | **Page Object Model** — all selectors in one place |
| `Tests/SidebarAndNavigationTests.cs` | Sidebar file list, env pills, no-file state, nav switching, theme toggle |
| `Tests/CrudOperationTests.cs` | Add key, inline edit (Enter/Escape/blur), delete + confirm modal, search/filter, sensitive masking |
| `Tests/RawEditorTests.cs` | Tree↔Raw toggle, Monaco status badge, Ctrl+S save, Format, invalid JSON rejection |
| `Tests/SwapAndDiffTests.cs` | Swap panel key list, copy/move confirm modal, same-source guard; diff table rendering, identical files, reset on nav |
| `Tests/BackupAndAuditTests.cs` | Sidebar open/close, create backup (default + custom name), diff modal, restore modal, delete backup, audit tab |
| `Tests/AdvancedFeatureTests.cs` | Array add/append/delete badge, schema banner dismiss, assertion runner, read-only mode, refresh, welcome stats |

---

## Adding tests for a new design

1. **Never hardcode selectors in test files** — add new locators to `Pages/ConfigUIPage.cs`.
2. Each test calls `fixture.NewPageAsync()` which navigates to `/config-ui` fresh — tests are stateless by design.
3. Config files are re-seeded per fixture instantiation (once per test run). If a test needs a clean slate mid-run, write to `fixture.ConfigDirectory` directly.
4. Keep assertions specific: prefer `contain("AllowedHosts")` over `not be empty()` — vague assertions pass on broken UIs that still render something.

---

## CI integration (GitHub Actions example)

```yaml
- name: Install .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'

- name: Build
  run: dotnet build --configuration Release

- name: Install Playwright browsers
  run: pwsh tests/Codekali.Net.Config.UI.UITests/playwright.config.ps1

- name: Start sample app
  run: dotnet run --project samples/SampleWebApp --urls http://localhost:5100 &
  env:
    ASPNETCORE_ENVIRONMENT: Development
    CONFIGUI_CONFIG_DIRECTORY: /tmp/uitest-configs

- name: Seed test config files
  run: |
    mkdir -p /tmp/uitest-configs
    # copy seeded JSON from fixtures or echo inline

- name: Run UI tests
  run: dotnet test tests/Codekali.Net.Config.UI.UITests --logger "trx;LogFileName=results.trx"
  env:
    CONFIGUI_TEST_URL: http://localhost:5100

- name: Upload test results
  if: always()
  uses: actions/upload-artifact@v4
  with:
    name: ui-test-results
    path: "**/*.trx"
```
