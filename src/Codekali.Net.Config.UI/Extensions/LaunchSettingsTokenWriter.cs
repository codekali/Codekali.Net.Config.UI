using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Extensions;

/// <summary>
/// Generates a cryptographically random access token for the Config UI endpoint
/// and persists it into <c>Properties/launchSettings.json</c> under the
/// <c>environmentVariables</c> section of every profile, so the token is available
/// at runtime via <c>IConfiguration</c> / environment variables without being
/// hard-coded in source files.
/// </summary>
/// <remarks>
/// <para>
/// The token is written to <c>launchSettings.json</c> rather than
/// <c>appsettings.json</c> for two reasons:
/// <list type="bullet">
///   <item><c>launchSettings.json</c> is loaded only by the local development
///   tooling (dotnet run / Visual Studio / Rider) and is conventionally excluded
///   from production deployments.</item>
///   <item>Environment variables in <c>launchSettings.json</c> are surfaced by
///   <c>WebApplication.CreateBuilder()</c> automatically without any extra
///   configuration wiring.</item>
/// </list>
/// </para>
/// <para>
/// In production or CI environments the token should be supplied via a real
/// environment variable or a secrets manager; this service is a no-op when
/// <c>launchSettings.json</c> does not exist (e.g. on a build server).
/// </para>
/// <para>
/// Call <see cref="EnsureTokenAsync"/> once at application startup, before
/// <c>app.UseConfigUI()</c>. If a token already exists in
/// <c>launchSettings.json</c> the file is left untouched.
/// </para>
/// </remarks>
internal sealed class LaunchSettingsTokenWriter(
    string projectDirectory,
    ILogger<LaunchSettingsTokenWriter> logger)
{
    /// <summary>
    /// The environment variable name written into <c>launchSettings.json</c>
    /// and read back by <see cref="Extensions.ServiceCollectionExtensions"/> when
    /// <see cref="Models.ConfigUIOptions.AccessToken"/> is not set explicitly.
    /// </summary>
    public const string EnvironmentVariableName = "CONFIGUI_ACCESS_TOKEN";

    private const string LaunchSettingsRelativePath = "Properties/launchSettings.json";

    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        WriteIndented = true
    };

    private readonly string _launchSettingsPath = Path.Combine(projectDirectory, LaunchSettingsRelativePath);

    /// <summary>
    /// Ensures a Config UI access token exists in <c>launchSettings.json</c>.
    /// </summary>
    /// <returns>
    /// The existing token if one was already present, or the newly generated token
    /// if one was written.  Returns <c>null</c> if <c>launchSettings.json</c> does
    /// not exist (e.g. production / CI environment — caller should supply the token
    /// via a real environment variable instead).
    /// </returns>
    public async Task<string?> EnsureTokenAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_launchSettingsPath))
        {
            logger.LogDebug(
                "launchSettings.json not found at {Path} — skipping token generation.",
                _launchSettingsPath);
            return null;
        }

        var raw = await File.ReadAllTextAsync(_launchSettingsPath, ct).ConfigureAwait(false);

        JsonObject root;
        try
        {
            root = JsonNode.Parse(raw) as JsonObject
                ?? throw new InvalidOperationException("Root of launchSettings.json is not a JSON object.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not parse launchSettings.json — skipping token generation.");
            return null;
        }

        // ── Check whether a token already exists in ANY profile ──────────
        var profiles = root["profiles"] as JsonObject;
        if (profiles is not null)
        {
            foreach (var profile in profiles)
            {
                var envVars = profile.Value?["environmentVariables"] as JsonObject;
                if (envVars is null) continue;

                if (envVars.TryGetPropertyValue(EnvironmentVariableName, out var existing)
                    && existing is JsonValue jv
                    && jv.TryGetValue<string>(out var existingToken)
                    && !string.IsNullOrWhiteSpace(existingToken))
                {
                    logger.LogDebug(
                        "Config UI access token already present in launchSettings.json profile '{Profile}'.",
                        profile.Key);
                    return existingToken;
                }
            }
        }

        // ── Generate a new token ──────────────────────────────────────────
        var newToken = GenerateToken();

        // ── Write the token into every profile's environmentVariables ─────
        if (profiles is not null)
        {
            foreach (var profile in profiles)
            {
                var profileObj = profile.Value as JsonObject;
                if (profileObj is null) continue;

                if (profileObj["environmentVariables"] is not JsonObject envVars)
                {
                    envVars = new JsonObject();
                    profileObj["environmentVariables"] = envVars;
                }

                envVars[EnvironmentVariableName] = newToken;
            }
        }
        else
        {
            // No profiles section — unlikely, but handle gracefully
            logger.LogWarning(
                "launchSettings.json has no 'profiles' section. Token was not written.");
            return newToken;
        }

        // ── Persist ───────────────────────────────────────────────────────
        var updated = root.ToJsonString(_writeOpts);
        await File.WriteAllTextAsync(_launchSettingsPath, updated, ct).ConfigureAwait(false);

        logger.LogInformation(
            "Generated Config UI access token and wrote it to launchSettings.json " +
            "as environment variable '{EnvVar}'. " +
            "Copy this token to options.AccessToken or supply it as an environment variable in production.",
            EnvironmentVariableName);

        return newToken;
    }

    /// <summary>
    /// Generates a URL-safe, cryptographically random token (32 bytes → 43 Base64Url chars).
    /// </summary>
    private static string GenerateToken()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        // Base64Url encoding — no padding, safe for use in HTTP headers and query strings
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}