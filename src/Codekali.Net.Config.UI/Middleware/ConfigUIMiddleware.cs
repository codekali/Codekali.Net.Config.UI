using Codekali.Net.Config.UI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Codekali.Net.Config.UI.Middleware;

/// <summary>
/// ASP.NET Core middleware that serves the Config UI browser panel and a lightweight
/// JSON API consumed by that panel. All business logic is delegated to injected services —
/// this class is intentionally thin (routing + serialisation only).
/// </summary>
internal sealed class ConfigUIMiddleware(
    ConfigUIApiHandler configUIApiHandler,
    ConfigUIStaticHandler configUIStaticHandler,
    RequestDelegate next,
    //ILogger<ConfigUIMiddleware> logger,
    ConfigUIOptions options)
{
    /// <summary>Invokes the middleware for each HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(options.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // ── Environment guard ──────────────────────────────────────────────
        var env = context.RequestServices.GetService<IHostEnvironment>();
        if (!ConfigUIMiddlewareHelpers.IsAllowedEnvironment(env?.EnvironmentName, options))
        {
            context.Response.StatusCode = 404;
            return;
        }

        // ── Token guard ────────────────────────────────────────────────────
        if (!ConfigUIMiddlewareHelpers.IsAuthorised(context.Request, options))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized — supply the correct access token.", context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        // ── Read-only guard for mutations ──────────────────────────────────
        var subPath = path[options.PathPrefix.Length..].TrimStart('/');

        if (options.ReadOnly && ConfigUIMiddlewareHelpers.IsMutationRequest(context.Request.Method))
        {
            context.Response.StatusCode = 403;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(context, new { error = "Config UI is in read-only mode." })
                .ConfigureAwait(false);
            return;
        }

        // ── Route ──────────────────────────────────────────────────────────
        await RouteAsync(context, subPath).ConfigureAwait(false);
    }

    // ── Routing ─────────────────────────────────────────────────────────────

    private async Task RouteAsync(HttpContext ctx, string subPath)
    {
        var method = ctx.Request.Method.ToUpperInvariant();

        // Static assets: css, js, icons
        if (subPath.StartsWith("static/", StringComparison.OrdinalIgnoreCase) ||
            subPath.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            subPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            subPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            await ConfigUIStaticHandler.ServeEmbeddedResourceAsync(ctx, subPath).ConfigureAwait(false);
            return;
        }

        // ── API routes (/config-ui/api/*) ──
        if (subPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await configUIApiHandler.HandleApiAsync(ctx, subPath[4..], method).ConfigureAwait(false);
            return;
        }

        // ── Fallback: serve the SPA shell ──
        await configUIStaticHandler.ServeIndexHtmlAsync(ctx).ConfigureAwait(false);
    }
}