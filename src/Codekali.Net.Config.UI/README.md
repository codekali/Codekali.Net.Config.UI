# Codekali.Net.Config.UI — Directions of Use

A minimal guide to install, configure, and run the in-app configuration UI.

## 1. Install

```bash
dotnet add package Codekali.Net.Config.UI
```

## 2. Register services (Program.cs)

```csharp
builder.Services.AddConfigUI();
```

## 3. Activate middleware

```csharp
app.UseConfigUI();
```

## 4. Access the UI

Open the configured path in a browser:

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

## 5. Recommended runtime considerations

- By default the UI is intended for development; restrict `AllowedEnvironments` for production.
- Always set `AccessToken` for non-development use, or host behind internal network/VPN.

---

## 🔐 Security

The middleware returns **404** when accessed outside an allowed environment — it does not leak its existence. For production usage:

- Set `AllowedEnvironments` explicitly
- Always set an `AccessToken`
- Consider putting the path behind a VPN or internal network

---

## 📦 NuGet Targets

| Target           | Notes                              |
|------------------|------------------------------------|
| `net8.0`         | Full ASP.NET Core FrameworkRef     |
| `netstandard2.1` | Works with any compatible host     |

---

*Codekali.Net.Config.UI — github.com/Codekali*