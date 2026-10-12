using Codekali.Net.Config.UI.Interfaces;

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
    /// <para>
    /// To enable automatic secure-token generation on first startup, set
    /// <see cref="EnableAutoToken"/> to <c>true</c>. The generated token is written
    /// to <c>Properties/launchSettings.json</c> and loaded from the
    /// <c>CONFIGUI_ACCESS_TOKEN</c> environment variable on subsequent runs.
    /// </para>
    /// </remarks>
    public string? AccessToken { get; set; }

    /// <summary>
    /// When <c>true</c>, automatically generates a cryptographically random access
    /// token on the first startup and persists it in <c>Properties/launchSettings.json</c>
    /// under the <c>CONFIGUI_ACCESS_TOKEN</c> environment variable key.
    /// Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On subsequent runs the token is read from the <c>CONFIGUI_ACCESS_TOKEN</c>
    /// environment variable, which <c>launchSettings.json</c> injects automatically
    /// in local development.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// builder.Services.AddConfigUI(options =>
    /// {
    ///     options.EnableAutoToken = true;
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public bool EnableAutoToken { get; set; } = false;

    /// <summary>
    /// Optional ASP.NET Core authorization policy name to enforce on the Config UI.
    /// When set, <c>IAuthorizationService.AuthorizeAsync</c> is called with this policy
    /// before serving any response.
    /// Works with any identity provider — ASP.NET Core Identity, Azure AD, Auth0,
    /// cookie schemes, or custom policies.
    /// The existing <see cref="AccessToken"/> mechanism is fully preserved;
    /// both can be used simultaneously.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Register the policy and apply it to the UI:
    /// <code>
    /// builder.Services.AddAuthorization(o =>
    ///     o.AddPolicy("ConfigUIAccess", p => p.RequireRole("Admin")));
    ///
    /// builder.Services.AddConfigUI(options =>
    ///     options.AuthorizationPolicy = "ConfigUIAccess");
    /// </code>
    /// </para>
    /// </remarks>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// When <c>true</c>, a dismissible banner is shown in the UI when an appsettings
    /// file is modified on disk by an external process.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool EnableHotReloadDetection { get; set; } = true;

    /// <summary>
    /// The absolute path to the directory that contains the appsettings files.
    /// Defaults to <see cref="Directory.GetCurrentDirectory"/> when <c>null</c>.
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
    /// When <c>null</c> (default), backups use a timestamp suffix.
    /// </summary>
    [System.ComponentModel.DataAnnotations.RegularExpression(
        @"^[a-zA-Z0-9._-]{2,30}$",
        ErrorMessage = "BackupVersionPrefix may only contain letters, numbers, period, hyphen, and underscores.")]
    public string? BackupVersionPrefix { get; set; }

    /// <summary>
    /// When <c>true</c>, every write operation (Add, Update, Delete, SaveRaw, etc.)
    /// is appended to a per-file <c>.audit.json</c> log alongside the appsettings file.
    /// Defaults to <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usage:
    /// <code>
    /// builder.Services.AddConfigUI(options =>
    /// {
    ///     options.EnableAuditLogging = true;
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public bool EnableAuditLogging { get; set; } = false;

    /// <summary>
    /// When <c>true</c> and <see cref="EnableAuditLogging"/> is also <c>true</c>,
    /// audit entries are additionally forwarded to <c>ILogger</c> at Information level.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool ForwardAuditToLogger { get; set; } = false;

    /// <summary>
    /// Absolute or relative path to a JSON Schema file (draft-07 subset).
    /// When set, the UI validates the current file against the schema on load
    /// and before every save, surfacing violations inline in the tree view.
    /// Relative paths are resolved against <see cref="ConfigDirectory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema is also passed to Monaco Editor to enable live key-level
    /// autocomplete and hover documentation in the raw editor at no additional cost.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// builder.Services.AddConfigUI(options =>
    /// {
    ///     options.SchemaPath = "appsettings.schema.json";
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public string? SchemaPath { get; set; }

    /// <summary>
    /// Inline JSON Schema string (draft-07 subset). Takes precedence over
    /// <see cref="SchemaPath"/> when both are set.
    /// Useful for embedding a schema directly in <c>Program.cs</c> without
    /// requiring a separate schema file on disk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usage:
    /// <code>
    /// builder.Services.AddConfigUI(options =>
    /// {
    ///     options.SchemaJson = """
    ///         {
    ///           "type": "object",
    ///           "required": ["ConnectionStrings"],
    ///           "properties": {
    ///             "ConnectionStrings": { "type": "object" },
    ///             "Logging": { "type": "object" }
    ///           }
    ///         }
    ///         """;
    /// });
    /// </code>
    /// </para>
    /// </remarks>
    public string? SchemaJson { get; set; }

    /// <summary>
    /// Types implementing <see cref="IConfigurationTest"/> to register for the
    /// Config UI Test Runner panel. The library registers and instantiates these
    /// types automatically — no manual DI registration is required.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implement <see cref="IConfigurationTest"/> in your project, then pass the
    /// type here:
    /// <code>
    /// builder.Services.AddConfigUI(o =>
    /// {
    ///     o.ConfigurationTestTypes = [typeof(MyAppConfigTests), typeof(ProductionReadinessTests)];
    /// });
    /// </code>
    /// </para>
    /// <para>
    /// Each type is registered as a singleton via <c>IConfigurationTest</c> and
    /// resolved by <see cref="IAssertionRunnerService"/> at test-run time. Tests
    /// are run on demand from the Test Runner panel — never automatically at startup.
    /// </para>
    /// <para>
    /// <b>Constraints:</b>
    /// <list type="bullet">
    ///   <item>Types must implement <see cref="IConfigurationTest"/> or an
    ///   <see cref="InvalidOperationException"/> is thrown at startup.</item>
    ///   <item>Test classes must have a public parameterless constructor.
    ///   Constructor injection is not supported — use the <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
    ///   passed into <see cref="IConfigurationTest.RunAsync"/> instead.</item>
    ///   <item>Tests run sequentially in the order they are declared in this list.</item>
    ///   <item>Any unhandled exception thrown by a test is caught and surfaced as a
    ///   failed result — it will not crash the middleware.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public IReadOnlyList<Type> ConfigurationTestTypes { get; set; } = [];
}