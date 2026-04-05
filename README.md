# Codekali.Net.Config.UI

> A plug-and-play .NET 8 library that provides a browser-based GUI for managing `appsettings.json` configuration files in any .NET solution.

---

## ✨ Features

- 📂 **Auto-detect** all `appsettings*.json` files at runtime
- 🌳 **Tree view** — expand, edit, add, delete inline with full array support
- 🔢 **Array editing** — expand arrays, append items, remove items by index
- ✏️ **Raw JSON editor** powered by Monaco (VS Code engine) with syntax highlighting, bracket matching, and `Ctrl+S` to save
- 💬 **Comment preservation** — `//` and `/* */` comments survive every save
- ⇄ **Move / Copy** configuration keys between environment files
- ⊞ **Side-by-side diff** comparing any two environment files
- 🔄 **Hot reload detection** — banner notification when a file changes externally
- 💾 **Never auto-saves** — all changes require an explicit save action
- 🔒 **Sensitive value masking** (`password`, `secret`, `token`, `apikey`)
- 🌙 **Dark / Light mode** toggle
- 🔐 **Development-only** by default — optional access token or ASP.NET Core Authorization policy

---

## 🚀 Quick Start

### 1. Install

```bash
dotnet add package Codekali.Net.Config.UI
```

### 2. Register services

```csharp
// Program.cs
builder.Services.AddConfigUI();
```

### 3. Activate middleware

```csharp
app.UseConfigUI();
```

### 4. Open your browser

```
https://localhost:5001/config-ui
```

---

## ⚙️ Configuration

```csharp
builder.Services.AddConfigUI(options =>
{
    options.PathPrefix            = "/config-ui";
    options.AccessToken           = "your-secret-token";     // simple token auth
    options.AuthorizationPolicy   = "ConfigUIAccess";         // or ASP.NET Core policy
    options.AllowedEnvironments   = ["Development","Staging"];
    options.MaskSensitiveValues   = true;
    options.ReadOnly              = false;
    options.EnableAutoToken       = false;                    // auto-generate token on first run
    options.EnableHotReloadDetection = true;
    options.ConfigDirectory       = null;                     // defaults to CWD
});
```

### Authorization policy example
```csharp
builder.Services.AddAuthorization(o =>
    o.AddPolicy("ConfigUIAccess", p => p.RequireRole("Admin")));

app.UseConfigUI(options => options.AuthorizationPolicy = "ConfigUIAccess");
```

### Auto token generation
```csharp
builder.Services.AddConfigUI(options => options.EnableAutoToken = true);
// Token is generated on first run and written to Properties/launchSettings.json
// as CONFIGUI_ACCESS_TOKEN. Subsequent runs load it from the environment variable.
```

### IOptions reload guidance

> ⚠️ Changes written by the Config UI take effect at runtime only when consuming code uses `IOptionsSnapshot<T>` (per-request) or `IOptionsMonitor<T>` (singleton-safe) rather than `IOptions<T>` which snapshots at startup.

Access token is checked via:
- Header: `X-Config-Token: your-secret-token`
- Query string: `?token=your-secret-token`

---

## 🔐 Security

The middleware returns **404** when accessed outside an allowed environment — it does not leak its existence. For production usage:

- Set `AllowedEnvironments` explicitly
- Always set an `AccessToken`
- Consider putting the path behind a VPN or internal network

---

## 📁 Project Structure

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

samples/SampleWebApp/   Reference project
tests/                  xUnit + Moq + FluentAssertions
```

---

## 🛠 Development

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run sample app
cd samples/SampleWebApp
dotnet run
# → open https://localhost:5001/config-ui
```

---

## 📦 NuGet Targets

| Target           | Notes                              |
|------------------|------------------------------------|
| `net8.0`         | Full ASP.NET Core FrameworkRef     |
| `netstandard2.1` | Works with any compatible host     |

---

*Codekali.Net.Config.UI — github.com/Codekali*
