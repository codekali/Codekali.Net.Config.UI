# Codekali.Net.Config.UI

> A plug-and-play .NET 8 library that provides a browser-based GUI for managing `appsettings.json` configuration files in any .NET solution.

---

## ✨ Features

- 📂 **Auto-detect** all `appsettings*.json` files at runtime
- 🌳 **Tree view** for nested JSON — expand, edit, delete inline
- ✏️ **Raw JSON editor** with validation and format-on-demand
- ⇄ **Move / Copy** configuration keys between environment files
- ⊞ **Side-by-side diff** comparing any two environment files
- 💾 **Never auto-saves** — all changes require an explicit save action
- 🔒 **Auto-backup** before every write (`.bak` files alongside originals)
- 🙈 **Sensitive value masking** (`password`, `secret`, `token`, `key`)
- 🌙 **Dark / Light mode** toggle
- 🔐 **Development-only** by default — optional access token protection

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
    options.PathPrefix          = "/config-ui";            // UI URL
    options.AccessToken         = "your-secret-token";     // optional bearer token
    options.AllowedEnvironments = ["Development","Staging"]; // ["*"] = all envs
    options.MaskSensitiveValues = true;                    // mask password/secret/token/key
    options.ReadOnly            = false;                   // prevent all edits
    options.ConfigDirectory     = null;                    // defaults to CWD
});
```

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
