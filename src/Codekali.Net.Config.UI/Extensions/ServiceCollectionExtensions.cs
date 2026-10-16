using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Middleware;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Codekali.Net.Config.UI.Extensions;

/// <summary>
/// Extension methods for registering Codekali.Net.Config.UI services and middleware.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Config UI services with the dependency injection container.
    /// </summary>
    /// <remarks>
    /// Three usage patterns:
    /// <code>
    /// // 1. Zero-config — open in Development, no token:
    /// builder.Services.AddConfigUI();
    ///
    /// // 2. Explicit token:
    /// builder.Services.AddConfigUI(o => o.AccessToken = "my-secret");
    ///
    /// // 3. Auto-generate token on first run, persist to launchSettings.json:
    /// builder.Services.AddConfigUI(o => o.EnableAutoToken = true);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddConfigUI(
        this IServiceCollection services,
        Action<ConfigUIOptions>? configure = null)
    {
        var options = new ConfigUIOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddSingleton<IConfigFileRepository, ConfigFileRepository>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IEnvironmentSwapService, EnvironmentSwapService>();
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<ConfigUIApiHandler>();
        services.AddSingleton<ConfigUIStaticHandler>(); 
        services.AddSingleton<ISchemaValidationService, SchemaValidationService>();
        services.AddSingleton<IAssertionRunnerService, AssertionRunnerService>();
        
        foreach (var type in options.ConfigurationTestTypes)
        {
            if (!typeof(IConfigurationTest).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' does not implement IConfigurationTest.");

            services.AddSingleton(typeof(IConfigurationTest), type);
        }

        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(options, new ValidationContext(options), results, true))
            throw new InvalidOperationException(results[0].ErrorMessage);

        return services;
    }

    /// <summary>Adds the Config UI middleware to the ASP.NET Core request pipeline.</summary>
    /// <remarks>
    /// Placing `UseConfigUI()` after `UseAuthorization()` will result in `401 Unauthorized`
    /// responses if your app has a default authorization policy, or unexpected behavior if
    /// your app uses authentication middleware that rejects unauthenticated requests.
    /// </remarks>
    public static IApplicationBuilder UseConfigUI(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<ConfigUIOptions>();
        
        if (options.EnableAutoToken)
            ResolveAutoToken(app, options);

        app.UseMiddleware<ConfigUIMiddleware>();
        return app;
    }

    // ── Auto-token resolution (only called when EnableAutoToken = true) ───────

    /// <summary>
    /// Resolves the access token in priority order:
    /// <list type="number">
    ///   <item>Explicit <see cref="ConfigUIOptions.AccessToken"/> already set — used as-is.</item>
    ///   <item><c>CONFIGUI_ACCESS_TOKEN</c> user-level environment variable — used automatically
    ///         on subsequent runs after the token was generated and persisted on first startup.</item>
    ///   <item>Auto-generate a new token, set it in the current process, and persist it as a
    ///         user-level environment variable so it survives restarts without any manual steps.
    ///         Falls back to process-level only if the user-level write fails (e.g. containers,
    ///         restricted CI environments).</item>
    /// </list>
    /// This method is intentionally only reachable when
    /// <see cref="ConfigUIOptions.EnableAutoToken"/> is <c>true</c>.
    /// </summary>
    private static void ResolveAutoToken(IApplicationBuilder app, ConfigUIOptions options)
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(ServiceCollectionExtensions));

        if (!string.IsNullOrWhiteSpace(options.AccessToken))
            return;

        var envToken = Environment.GetEnvironmentVariable("CONFIGUI_ACCESS_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            options.AccessToken = envToken;
            logger.LogInformation(
                "[ConfigUI] Access token loaded from CONFIGUI_ACCESS_TOKEN environment variable.");
            return;
        }

        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

        options.AccessToken = token;

        // Set in current process immediately
        Environment.SetEnvironmentVariable("CONFIGUI_ACCESS_TOKEN", token);

        // Persist at user level so it survives restarts without developer intervention
        try
        {
            Environment.SetEnvironmentVariable(
                "CONFIGUI_ACCESS_TOKEN",
                token,
                EnvironmentVariableTarget.User);

            logger.LogInformation(
                "[ConfigUI] Generated access token and persisted to user-level environment variable " +
                "'CONFIGUI_ACCESS_TOKEN'. Token is active immediately and will persist across restarts.");
        }
        catch (Exception ex)
        {
            // User-level env var write can fail on restricted environments (containers, some CI systems)
            // Fall back to process-level only — token works this run but won't survive restart
            logger.LogWarning(
                "[ConfigUI] Could not persist token to user-level environment variable: {Reason}. " +
                "Token is active this run only. Set CONFIGUI_ACCESS_TOKEN manually for persistence.",
                ex.Message);
        }
    }
}