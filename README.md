# Codekali.Net.Config.UI

> Browser-based configuration management UI for ASP.NET Core. Edit `appsettings.json` files across environments with a visual tree editor, Monaco code editor, and comprehensive audit trail.

---

## Features

- **Auto-detect** all `appsettings*.json` files at runtime
- **Tree view editor** — expand, edit, add, delete inline with full array support
- **Array handling** — expand arrays, append items, remove items by index
- **Monaco editor** (VS Code engine) — syntax highlighting, bracket matching, `Ctrl+S` to save
- **Comment preservation** — `//` and `/* */` comments survive every save
- **Copy/Move keys** — reorganize configuration keys between environment files
- **Side-by-side diff** — compare any two environment files
- **Hot reload detection** — banner notification when a file changes externally
- **Never auto-saves** — all changes require an explicit save action
- **Sensitive value masking** — automatically mask passwords, secrets, tokens, API keys
- **Dark/Light mode** — toggle between themes
- **Timestamped backups** — automatic backup files with version prefixes (e.g., `appsettings.json.v1.2.1.20240601T1530.bak`)
- **Audit logs** — track all changes to appsettings files, including raw JSON saves
- **Development-only by default** — optional access token or ASP.NET Core Authorization policy

---

## 🚀 Quick Start

### Install

```bash
dotnet add package Codekali.Net.Config.UI
```

### Register Services

```csharp
// Program.cs
builder.Services.AddConfigUI();
```

### Activate Middleware
```csharp
app.UseConfigUI();
```

### Open in Browser

```
https://localhost:{PORT}/config-ui
```

---

## Configuration

```csharp
builder.Services.AddConfigUI(options =>
{
    options.PathPrefix            = "/config-ui";            // Configurable, defaults to "/config-ui"
    options.AccessToken           = "your-secret-token";     // Simple token-based authentication
    options.AuthorizationPolicy   = "ConfigUIAccess";        // OR ASP.NET Core authorization policy
    options.AllowedEnvironments   = ["Development","Staging"];
    options.MaskSensitiveValues   = true;
    options.ReadOnly              = false;
    options.EnableAutoToken       = false;                   // Auto-generate token on first run
    options.EnableHotReloadDetection = true;
    options.ConfigDirectory       = null;                    // Defaults to current working directory
    options.ShowReloadWarning     = true;
    options.BackupVersionPrefix   = "v1";                    // Optional version prefix for backups
    options.EnableAuditLogging    = true;   
});
```

### Authorization Policy Example
```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("ConfigUIAccess", p => p.RequireRole("Admin")));

app.UseConfigUI();
```

### Auto Token Generation
```csharp
builder.Services.AddConfigUI(options => options.EnableAutoToken = true);
// Token is generated on first run and written to Properties/launchSettings.json
// as CONFIGUI_ACCESS_TOKEN. Subsequent runs load it from the environment variable.
```

### Important: IOptions Reload Guidance

**Note:** Changes written by Config UI take effect at runtime only when consuming code uses `IOptionsSnapshot<T>` (per-request) or `IOptionsMonitor<T>` (singleton-safe) rather than `IOptions<T>` which snapshots at startup.

Access token is checked via:
- Header: `X-Config-Token: your-secret-token`
- Query string: `?token=your-secret-token`

---

## Security

The middleware returns **404** when accessed outside an allowed environment — it does not leak its existence. For production usage:

- Set `AllowedEnvironments` explicitly to restrict deployment targets
- Always configure an `AccessToken` for authentication
- Consider putting the path behind a VPN or firewall for additional protection

---

## Project Structure

```
src/Codekali.Net.Config.UI/
├── Middleware/         ConfigUIMiddleware.cs
├── Services/           AppSettingsService, BackupService, EnvironmentSwapService
│                       ConfigFileRepository, JsonHelper
├── Interfaces/         IAppSettingsService, IBackupService,
│                       IEnvironmentSwapService, IConfigFileRepository
├── Models/             ConfigEntry, AppSettingsFile, SwapRequest,
│                       OperationResult, ConfigUIOptions, DiffResult
├── Extensions/         ServiceCollectionExtensions (AddConfigUI / UseConfigUI)
└── UI/wwwroot/         index.html (embedded single-page UI)

samples/SampleWebApp/   Reference application
tests/                  Unit tests (xUnit, Moq, FluentAssertions)
```

---

## Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run sample application
cd samples/SampleWebApp
dotnet run
# → open https://localhost:5001/config-ui
```

---

## Target Frameworks

| Target           | Details                              |
|------------------|--------------------------------------|
| `net8.0`         | Full ASP.NET Core framework          |
| `netstandard2.1` | Compatible with any .NET host        |

---

## Version History

### v1.2.1 (Latest)
- Timestamped backups — backup files now include timestamp and optional version prefix (e.g., `appsettings.json.v1.2.1.20240601T1530.bak`)
- Audit logs — track changes to appsettings files, including raw JSON saves for full transparency

### v1.1.6
- Comment preservation — `//` and `/* */` comments survive every save
- Copy/Move configuration keys between environment files
- Side-by-side diff comparing any two environment files
- Hot reload detection — banner notification when a file changes externally
- Explicit save requirement — never auto-saves configuration changes
- Sensitive value masking — automatically mask passwords, API keys, tokens
- Dark/Light mode — switch between themes

### v1.1.0
- Enhanced feature set — refined UI/UX for configuration management
- Improved stability — better error handling and edge case coverage
- Better documentation — clearer configuration and usage examples

### v1.0.0
- Initial release with core configuration UI features

---

Made with ❤️ by [Codekali](https://github.com/Codekali) | Licensed under MIT
