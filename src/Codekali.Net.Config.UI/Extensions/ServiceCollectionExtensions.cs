using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Middleware;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        services.AddSingleton<ConfigUIApiHandler>();
        services.AddSingleton<ConfigUIStaticHandler>();

        return services;
    }

    /// <summary>Adds the Config UI middleware to the ASP.NET Core request pipeline.</summary>
    public static IApplicationBuilder UseConfigUI(this IApplicationBuilder app)
    {
        // Token resolution is only attempted when the developer has opted in.
        // A plain UseConfigUI() call leaves AccessToken null → no token required.
        var options = app.ApplicationServices.GetRequiredService<ConfigUIOptions>();
        if (options.EnableAutoToken)
            ResolveAutoToken(app, options);

        app.UseMiddleware<ConfigUIMiddleware>();
        return app;
    }

    /// <summary>Adds the Config UI middleware with inline option overrides.</summary>
    public static IApplicationBuilder UseConfigUI(
        this IApplicationBuilder app,
        Action<ConfigUIOptions> configure)
    {
        var options = app.ApplicationServices.GetRequiredService<ConfigUIOptions>();
        configure(options);

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
    ///   <item><c>CONFIGUI_ACCESS_TOKEN</c> environment variable — covers production / CI and
    ///         subsequent local runs after the token was written to <c>launchSettings.json</c>.</item>
    ///   <item>Auto-generate a new token and persist it to <c>Properties/launchSettings.json</c>
    ///         (Development only, first run only).</item>
    /// </list>
    /// This method is intentionally only reachable when
    /// <see cref="ConfigUIOptions.EnableAutoToken"/> is <c>true</c>.
    /// </summary>
    private static void ResolveAutoToken(IApplicationBuilder app, ConfigUIOptions options)
    {
        var logger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(ServiceCollectionExtensions));

        // Priority 1 — explicit token already set in code; nothing to do.
        if (!string.IsNullOrWhiteSpace(options.AccessToken))
        {
            logger.LogDebug("[ConfigUI] AccessToken set explicitly — auto-token resolution skipped.");
            return;
        }

        // Priority 2 — environment variable.
        // On the second and subsequent runs the token that was written to
        // launchSettings.json on the first run will be present here.
        var envToken = Environment.GetEnvironmentVariable(
            LaunchSettingsTokenWriter.EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(envToken))
        {
            options.AccessToken = envToken;
            logger.LogInformation(
                "[ConfigUI] AccessToken loaded from environment variable '{EnvVar}'.",
                LaunchSettingsTokenWriter.EnvironmentVariableName);
            return;
        }

        // Priority 3 — generate a new token (first run, Development only).
        var env = app.ApplicationServices.GetService<IHostEnvironment>();
        if (env is null || !env.IsDevelopment())
        {
            logger.LogWarning(
                "[ConfigUI] EnableAutoToken is true but the current environment is '{Env}'. " +
                "Auto-generation is only supported in Development. " +
                "Supply '{EnvVar}' as a real environment variable or set options.AccessToken explicitly.",
                env?.EnvironmentName ?? "unknown",
                LaunchSettingsTokenWriter.EnvironmentVariableName);
            return;
        }

        var writerLogger = app.ApplicationServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger<LaunchSettingsTokenWriter>();

        var writer = new LaunchSettingsTokenWriter(env.ContentRootPath, writerLogger);

        var token = writer.EnsureTokenAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (!string.IsNullOrWhiteSpace(token))
        {
            options.AccessToken = token;
            logger.LogInformation(
                "[ConfigUI] Generated access token: '{Token}'. " +
                "Saved to Properties/launchSettings.json under '{EnvVar}'. " +
                "Append ?token={Token} to the URL or send the X-Config-Token header.",
                token, LaunchSettingsTokenWriter.EnvironmentVariableName, token);
        }
    }
}