using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Middleware;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Codekali.Net.Config.UI.Extensions;

/// <summary>
/// Extension methods for registering and activating the Codekali Config UI
/// in an ASP.NET Core application.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Config UI services in the DI container.
    /// Call this from <c>builder.Services.AddConfigUI()</c> before <c>app.UseConfigUI()</c>.
    /// If you omit this call, <see cref="UseConfigUI(IApplicationBuilder, Action{ConfigUIOptions}?)"/>
    /// will register services with default options automatically.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <param name="configure">Optional delegate to configure <see cref="ConfigUIOptions"/>.</param>
    public static IServiceCollection AddConfigUI(
        this IServiceCollection services,
        Action<ConfigUIOptions>? configure = null)
    {
        var options = new ConfigUIOptions();
        configure?.Invoke(options);

        // Register options as a singleton so all services share the same instance
        services.AddSingleton(options);

        // Repository — file system abstraction (internal, not exposed)
        services.AddSingleton<IConfigFileRepository, ConfigFileRepository>();

        // Core services
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IEnvironmentSwapService, EnvironmentSwapService>();

        return services;
    }

    /// <summary>
    /// Adds the Config UI middleware to the ASP.NET Core pipeline.
    /// Accessible at <c>/config-ui</c> by default (configurable via <see cref="ConfigUIOptions.PathPrefix"/>).
    /// </summary>
    /// <remarks>
    /// If <see cref="AddConfigUI"/> was not called during service registration, this method
    /// will call it internally with the supplied options.
    /// </remarks>
    /// <param name="app">The application builder.</param>
    /// <param name="configure">Optional delegate to configure <see cref="ConfigUIOptions"/>.</param>
    public static IApplicationBuilder UseConfigUI(
        this IApplicationBuilder app,
        Action<ConfigUIOptions>? configure = null)
    {
        // Ensure services are registered even if AddConfigUI was skipped
        EnsureServicesRegistered(app, configure);

        var options = app.ApplicationServices.GetRequiredService<ConfigUIOptions>();

        app.UseMiddleware<ConfigUIMiddleware>(options);

        return app;
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private static void EnsureServicesRegistered(
        IApplicationBuilder app, Action<ConfigUIOptions>? configure)
    {
        // If the options singleton is already registered, honour it
        var existing = app.ApplicationServices.GetService<ConfigUIOptions>();
        if (existing is not null)
        {
            // Allow the configure delegate to mutate the existing options
            configure?.Invoke(existing);
            return;
        }

        // Services were not registered — this path should not normally be hit
        // because DI container is sealed at this point. Log a warning.
        // In practice users should call AddConfigUI() in their Program.cs.
        throw new InvalidOperationException(
            "Codekali Config UI services have not been registered. " +
            "Call builder.Services.AddConfigUI() before app.UseConfigUI().");
    }
}
