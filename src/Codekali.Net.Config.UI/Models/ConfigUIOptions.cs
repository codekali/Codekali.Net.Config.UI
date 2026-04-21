namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Configuration options for the Codekali.Net.Config.UI middleware.
/// </summary>
public sealed class ConfigUIOptions
{
    /// <summary>
    /// The URL path prefix at which the Config UI is served.
    /// Defaults to <c>/config-ui</c>.
    /// </summary>
    public string PathPrefix { get; set; } = "/config-ui";

    /// <summary>
    /// The environments in which the Config UI is accessible.
    /// Defaults to <c>["Development"]</c>.
    /// Use <c>["*"]</c> to allow all environments (not recommended for production).
    /// </summary>
    public IReadOnlyList<string> AllowedEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// Optional access token that callers must supply via the
    /// <c>X-Config-Token</c> request header or the <c>?token=</c> query parameter.
    /// When left empty (the default) the UI is accessible to anyone who can reach
    /// the endpoint — rely on the <see cref="AllowedEnvironments"/> guard and
    /// network-level controls in that case.
    /// </summary>
    /// <remarks>
    /// To enable automatic secure-token generation on first startup, set
    /// <see cref="EnableAutoToken"/> to <c>true</c>. The generated token is written
    /// to <c>Properties/launchSettings.json</c> and loaded from the
    /// <c>CONFIGUI_ACCESS_TOKEN</c> environment variable on subsequent runs.
    /// </remarks>
    public string? AccessToken { get; set; }

    /// <summary>
    /// When <c>true</c>, will automatically generate a cryptographically random access token on the
    /// first startup and persist it in <c>Properties/launchSettings.json</c> under
    /// the <c>CONFIGUI_ACCESS_TOKEN</c> environment variable key.
    /// <para>
    /// On subsequent runs the token is read from the
    /// <c>CONFIGUI_ACCESS_TOKEN</c> environment variable (which
    /// <c>launchSettings.json</c> injects automatically in local development).
    /// </para>
    /// <para>
    /// Defaults to <c>false</c> so that a plain <c>AddConfigUI()</c> call with
    /// no explicit token continues to work with zero friction in Development.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddConfigUI(options =>
    /// {
    ///     options.EnableAutoToken = true;   // generate + persist token on first run
    /// });
    /// </code>
    /// </example>
    public bool EnableAutoToken { get; set; } = false;

    /// <summary>
    /// Optional ASP.NET Core authorization policy name to enforce on the Config UI.
    /// When set, <c>IAuthorizationService.AuthorizeAsync</c> is called with this policy
    /// before serving any response. Works with any identity provider — ASP.NET Core
    /// Identity, Azure AD, Auth0, cookie schemes, or custom policies.
    /// The existing <see cref="AccessToken"/> mechanism is fully preserved;
    /// both can be used simultaneously.
    /// </summary>
    /// <example>
    /// <code>
    /// // Register the policy:
    /// builder.Services.AddAuthorization(o =>
    ///     o.AddPolicy("ConfigUIAccess", p => p.RequireRole("Admin")));
    ///
    /// // Apply it to the UI:
    /// builder.Services.AddConfigUI(options =>
    ///     options.AuthorizationPolicy = "ConfigUIAccess");
    /// </code>
    /// </example>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// When <c>true</c>, a dismissible banner is shown in the UI when an appsettings
    /// file is modified on disk by an external process. The tree view optionally
    /// reloads automatically.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool EnableHotReloadDetection { get; set; } = true;

    /// <summary>
    /// The absolute path to the directory that contains the appsettings files.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when null.
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// When <c>true</c>, all write operations (Add, Update, Delete, Save Raw) are
    /// rejected with HTTP 403. The UI renders in a read-only display mode.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool ReadOnly { get; set; } = false;

    /// <summary>
    /// When <c>true</c>, values of keys whose names contain <c>password</c>,
    /// <c>secret</c>, <c>token</c>, <c>apikey</c>, or <c>connectionstring</c>
    /// are masked in the tree view until the user explicitly reveals them.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool MaskSensitiveValues { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, a dismissible banner is shown in the UI reminding
    /// developers to use <c>IOptionsSnapshot&lt;T&gt;</c> or
    /// <c>IOptionsMonitor&lt;T&gt;</c> for hot-reload support.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool ShowReloadWarning { get; set; } = true;

    /// <summary>
    /// Optional version prefix for named backups, e.g. <c>"v1"</c>.
    /// When set, sequential backups are named <c>appsettings.json.v1.0.bak</c>,
    /// <c>appsettings.json.v1.1.bak</c>, etc.
    /// When null (default), backups use a timestamp suffix.
    /// </summary>
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^[a-zA-Z0-9._-]{2,30}$", ErrorMessage = "BackupVersionPrefix may only contain letters, numbers, period, hyphen, and underscores.")]
    public string? BackupVersionPrefix { get; set; }

    /// <summary>
    /// When <c>true</c>, every write operation (Add, Update, Delete, SaveRaw, etc.)
    /// is appended to a per-file <c>.audit.json</c> log alongside the appsettings file.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = false;

    /// <summary>
    /// When <c>true</c> and <see cref="EnableAuditLogging"/> is also <c>true</c>,
    /// audit entries are additionally forwarded to <c>ILogger</c> at Information level.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool ForwardAuditToLogger { get; set; } = false;
}