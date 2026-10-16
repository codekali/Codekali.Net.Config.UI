# Codekali.Net.Config.UI

Browser-based configuration management UI for ASP.NET Core. Edit `appsettings.json` files across environments with a visual tree editor, Monaco code editor, JSON schema validation, and a full audit trail — all via a single NuGet package.

---

## Quick Start

```bash
dotnet add package Codekali.Net.Config.UI
```

```csharp
// Program.cs
builder.Services.AddConfigUI();
app.UseConfigUI();
```

Open `https://localhost:{PORT}/config-ui`.

---

## Features

| | |
|---|---|
| **Tree view editor** | Expand, edit, add, delete keys and nested objects inline |
| **Array support** | Append items, remove by index, expand in tree |
| **Monaco editor** | VS Code engine — syntax highlighting, `Ctrl+S`, bracket matching |
| **Comment preservation** | `//` and `/* */` comments survive every save |
| **Schema validation** | Validate against JSON Schema (draft-07) before save; live Monaco autocomplete |
| **Assertion runner** | Define `IConfigurationTest` checks; run on demand from the Tests panel |
| **Copy/Move keys** | Migrate keys between environment files with collision detection |
| **Side-by-side diff** | Compare any two environment files |
| **Versioned backups** | Sequential `.bak` files with history panel, diff preview, and one-click restore |
| **Audit log** | Append-only per-file change log with old/new values |
| **Hot reload detection** | Banner notification when a file changes externally |
| **Sensitive value masking** | Passwords, secrets, tokens masked by default |
| **Dark/Light mode** | Toggle in the UI |

---

## Configuration

```csharp
builder.Services.AddConfigUI(options =>
{
    options.PathPrefix               = "/config-ui";
    options.AccessToken              = "your-secret-token";
    options.AuthorizationPolicy      = "ConfigUIAccess";        // OR use AccessToken, not both
    options.AllowedEnvironments      = ["Development", "Staging"];   // defaults to ["Development"]
    options.MaskSensitiveValues      = true;
    options.ReadOnly                 = false;
    options.EnableAutoToken          = false;
    options.ConfigDirectory          = null;                    // defaults to cwd
    options.BackupVersionPrefix      = "v1";                   // e.g. produces v1.3.bak
    options.EnableAuditLogging       = true;
    options.ForwardAuditToLogger     = false;
    options.SchemaPath               = "appsettings.schema.json";
});
```

### Access token

Passed via header `X-Config-Token: <token>`.

### Auto Token (Development)

```csharp
builder.Services.AddConfigUI(o => o.EnableAutoToken = true);
```

A cryptographically random token is generated on first startup, persisted as a
**user-level environment variable** (`CONFIGUI_ACCESS_TOKEN`) automatically, and
reused on every subsequent restart. The token is active immediately — no manual
steps required.

If the user-level environment variable cannot be written (containers, restricted CI
environments), the token is active for the current run only and a warning is logged.
Set `CONFIGUI_ACCESS_TOKEN` manually in those environments for persistence.

Avoid storing the token in `appsettings.json` or `appsettings.Development.json` —
those files are managed by this library and should never contain secrets.

### Explicit Token

```csharp
builder.Services.AddConfigUI(o => o.AccessToken = "your-secret-token");
```

### ASP.NET Core Authorization policy

```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("ConfigUIAccess", p => p.RequireRole("Admin")));

builder.Services.AddConfigUI(options =>
    options.AuthorizationPolicy = "ConfigUIAccess");
```

---

## Middleware Order

Config UI must be registered **before** `UseAuthentication()` and `UseAuthorization()`
in your pipeline. Config UI handles its own token-based authentication internally and
does not participate in ASP.NET Core's auth pipeline.

```csharp
app.UseCors();

app.UseConfigUI(); // ← must come before auth

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
```
---

Placing `UseConfigUI()` after `UseAuthorization()` will result in `401 Unauthorized`
responses if your app has a default authorization policy, or unexpected behavior if
your app uses authentication middleware that rejects unauthenticated requests.

## Schema Validation

Supply a JSON Schema (draft-07 subset) to block saves on violations. The schema is also passed to Monaco for live autocomplete and hover documentation.

```csharp
builder.Services.AddConfigUI(options =>
    options.SchemaPath = "appsettings.schema.json");
```

Supported keywords: `type`, `required`, `properties`, `additionalProperties`, `minLength`, `maxLength`, `pattern`, `minimum`, `maximum`, `enum`, `minItems`, `maxItems`.

---

## Configuration Assertion Runner

Implement `IConfigurationTest` to define assertions against your live configuration. Tests are discovered via DI and runnable on demand from the Tests panel.

```csharp
public class ProductionChecks : IConfigurationTest
{
    public string Name => "Production Readiness";

    public Task<AssertionOutcome> RunAsync(IConfiguration config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config["ConnectionStrings:Default"]))
            return Task.FromResult(AssertionOutcome.Fail("ConnectionStrings:Default must not be empty."));

        return Task.FromResult(AssertionOutcome.Pass());
    }
}

// Register
builder.Services.AddSingleton<IConfigurationTest, ProductionChecks>();
```

---

## IOptions Reload Guidance

> ⚠️ Config UI writes directly to disk. Whether your running app picks up changes without a restart depends on how you consume `IConfiguration`.

| Pattern | Picks Up Changes? |
|---|---|
| `IOptions<T>` | ❌ No — restart required |
| `IOptionsSnapshot<T>` | ✅ Yes — per request (scoped services) |
| `IOptionsMonitor<T>` | ✅ Yes — singleton safe, use `CurrentValue` |

`WebApplication.CreateBuilder()` enables `reloadOnChange` for appsettings files by default in .NET 6+.

---

## Security

The middleware returns **404** outside an allowed environment — it does not expose its existence. For non-development use, always set `AllowedEnvironments` and configure either `AccessToken` or `AuthorizationPolicy`.

---

## Target Frameworks

`net8.0` · `netstandard2.1`

---

## What's New in v1.3

- **Schema Validation Engine** — JSON Schema draft-07 subset; violations block saves and appear inline in the tree view and Monaco editor
- **Configuration Assertion Runner** — `IConfigurationTest` interface with in-UI test runner panel

## v1.2

- **Versioned Backup System** — sequential `.bak` files per environment with history panel, diff preview, and one-click restore
- **Change Audit Log** — append-only `.audit.json` log per file; optionally forwarded to `ILogger`

---

[GitHub](https://github.com/Codekali/Codekali.Net.Config.UI) · [MIT License](https://github.com/Codekali/Codekali.Net.Config.UI/blob/main/LICENSE) · Made by [Codekali](https://github.com/Codekali)