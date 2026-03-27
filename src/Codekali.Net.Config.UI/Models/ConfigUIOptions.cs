namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Configuration options for the Codekali Config UI middleware.
/// Pass an instance to <c>app.UseConfigUI(options => ...)</c> to customise behaviour.
/// </summary>
public sealed class ConfigUIOptions
{
    /// <summary>
    /// The URL path at which the Config UI is served.
    /// Defaults to <c>/config-ui</c>.
    /// </summary>
    public string PathPrefix { get; set; } = "/config-ui";

    /// <summary>
    /// Optional bearer token that callers must supply via the <c>X-Config-Token</c> header
    /// or <c>?token=</c> query parameter to access the UI and API endpoints.
    /// When null or empty, no token check is performed.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// The ASPNETCORE_ENVIRONMENT values in which the middleware is active.
    /// Defaults to <c>["Development"]</c>.
    /// Set to <c>["*"]</c> to allow all environments (not recommended for production).
    /// </summary>
    public string[] AllowedEnvironments { get; set; } = ["Development"];

    /// <summary>
    /// The directory that is scanned for <c>appsettings*.json</c> files.
    /// Defaults to <see cref="Directory.GetCurrentDirectory()"/> at startup.
    /// </summary>
    public string? ConfigDirectory { get; set; }

    /// <summary>
    /// When true, sensitive key values (containing "password", "secret", "token", "key")
    /// are masked in the UI by default. Users can individually reveal values.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool MaskSensitiveValues { get; set; } = true;

    /// <summary>
    /// When true, the UI is rendered in read-only mode: no edits, saves, or deletes are permitted.
    /// Defaults to <c>false</c>.
    /// </summary>
    public bool ReadOnly { get; set; }
}
