# Codekali.Net.Config.UI

> Browser-based configuration management UI for ASP.NET Core. Edit `appsettings.json` files across environments with a visual tree editor, Monaco code editor, JSON schema validation, configuration test runner, and a comprehensive audit trail.

---

## Quick Start

```bash
dotnet add package Codekali.Net.Config.UI
```

```csharp
builder.Services.AddConfigUI();
app.UseConfigUI();
// → https://localhost:{PORT}/config-ui
```

Full configuration options and usage examples: see the [NuGet README](./README.md).

---

## Repository Structure

```
src/Codekali.Net.Config.UI/
├── Middleware/     ConfigUIMiddleware.cs, ConfigUIApiHandler.cs, ConfigUIStaticHandler.cs
├── Services/       AppSettingsService, BackupService, EnvironmentSwapService,
│                   AuditService, SchemaValidationService, AssertionRunnerService,
│                   ConfigFileRepository, JsonHelper
├── Interfaces/     IAppSettingsService, IBackupService, IEnvironmentSwapService,
│                   IConfigFileRepository, IAuditService, ISchemaValidationService,
│                   IAssertionRunnerService, IConfigurationTest
├── Models/         ConfigEntry, AppSettingsFile, SwapRequest, OperationResult,
│                   ConfigUIOptions, DiffResult, AuditEntry, SchemaViolation, AssertionResult
├── Extensions/     ServiceCollectionExtensions (AddConfigUI / UseConfigUI),
│                   JsonCommentPreservingWriter, LaunchSettingsTokenWriter
└── UI/wwwroot/     index.html, NetConfigUI.js, NetConfigUI.css (embedded SPA)

samples/SampleWebApp/   Reference application
tests/                  Unit & integration tests (xUnit, Moq, FluentAssertions)
```

---

## Development

```bash
# Build (Debug)
dotnet build

# Build Release (triggers asset minification)
dotnet build -c Release

# Run tests
dotnet test

# Run sample app
cd samples/SampleWebApp
dotnet run
# → https://localhost:5001/config-ui

# Pack NuGet
dotnet pack -c Release
```

> Asset minification (HTML, CSS, JS) runs automatically as part of the Release build via inline MSBuild tasks defined in the `.csproj`. No external toolchain required.

---

## Architecture Notes

**Read path** uses `System.Text.Json` — fast, allocation-efficient, no third-party dependency.

**Write path** uses `Newtonsoft.Json` with `CommentHandling.Load` so `//` and `/* */` developer comments survive every Add, Update, Delete, and Save Raw operation. This is positional string surgery — not DOM re-serialisation — to guarantee no formatting loss.

**Backup policy** — backups are not created automatically on every save. Users trigger them explicitly via the UI. The `IBackupService` is also called internally before environment swap operations.

**Middleware guard order** — static assets (CSS, JS) are served before the environment and token guards so the login page renders correctly on restricted environments.

---

## Version History

### v1.3.1 (Latest)
- Bug fixes and stability improvements for schema validation and assertion runner

### v1.3.0
- **Schema Validation Engine** — JSON Schema draft-07 subset; blocks saves on violations; schema fed to Monaco for live autocomplete and hover docs
- **Configuration Assertion Runner** — `IConfigurationTest` interface; DI-discoverable; on-demand from the Tests panel

### v1.2.x
- **Versioned Backup System** — sequential `.bak` files with `BackupVersionPrefix` support; history panel with key-level diff preview; one-click restore with automatic pre-restore backup
- **Change Audit Log** — append-only `.audit.json` per file; Audit sidebar tab; optional `ILogger` forwarding

### v1.1.x
- **Monaco Editor** — VS Code engine; JSON highlighting, bracket matching, schema autocomplete, `Ctrl+S`
- **Comment Preservation** — `//` and `/* */` survive all write operations via `JsonCommentPreservingWriter`
- **ASP.NET Core Identity Authorization** — `AuthorizationPolicy` option works with any identity provider
- **Hot Reload Detection** — `FileSystemWatcher`-backed banner on external file changes
- **Empty State Guidance Panel** — best practices panel when no file is selected

### v1.0.0
- Initial release — tree editor, raw editor, Copy/Move/Diff, sensitive value masking, dark/light mode, `.bak` backups, Development-only middleware, access token auth

---

## Roadmap

See [`Codekali_Net_Config_UI-3-Roadmap.docx`](./docs/) for the full v1.1–v3.0 feature roadmap including the planned global CLI tool (`dotnet tool install -g codekali-config`).

---

Made with ❤️ by [Codekali](https://github.com/Codekali) · MIT License