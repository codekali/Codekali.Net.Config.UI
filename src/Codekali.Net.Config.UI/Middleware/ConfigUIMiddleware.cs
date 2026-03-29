using Codekali.Net.Config.UI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    ConfigUIOptions options)
{
    /// <summary>Invokes the middleware for each HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Pass through requests that are not under our path prefix.
        if (!path.StartsWith(options.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var subPath = path[options.PathPrefix.Length..].TrimStart('/');

        // ── Static assets: served BEFORE any auth or environment checks ────
        //
        // CSS, JS, and icon files contain no sensitive data and must be
        // delivered to the browser so that the login / token-entry page can
        // render correctly. Blocking them behind the token guard produces a
        // broken blank page because the browser receives 401 for every asset.
        //
        // A static asset is identified by its subPath starting with "static/"
        // OR by its file extension (.css / .js / .ico / .png / .svg).
        // The HTML shell (/config-ui or /config-ui/) is intentionally excluded
        // here so that it remains behind the environment + token guards.
        if (IsStaticAsset(subPath))
        {
            await ConfigUIStaticHandler.ServeEmbeddedResourceAsync(context, subPath)
                .ConfigureAwait(false);
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
        //
        // Only active when options.AccessToken is non-empty.
        // A plain AddConfigUI() with no token configured skips this entirely.
        if (!ConfigUIMiddlewareHelpers.IsAuthorised(context.Request, options))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync(
                "Unauthorized — supply the correct access token via the " +
                "X-Config-Token request header or ?token= query parameter.",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        // ── Read-only guard for mutations ──────────────────────────────────
        if (options.ReadOnly && ConfigUIMiddlewareHelpers.IsMutationRequest(context.Request.Method))
        {
            context.Response.StatusCode = 403;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(context,
                new { error = "Config UI is in read-only mode." }).ConfigureAwait(false);
            return;
        }

        // ── Route ──────────────────────────────────────────────────────────
        await RouteAsync(context, subPath).ConfigureAwait(false);
    }

    // ── Routing ─────────────────────────────────────────────────────────────

    private async Task RouteAsync(HttpContext ctx, string subPath)
    {
        var method = ctx.Request.Method.ToUpperInvariant();

        // ── API routes (/config-ui/api/*) ──
        if (subPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await configUIApiHandler.HandleApiAsync(ctx, subPath[4..], method)
                .ConfigureAwait(false);
            return;
        }

        // ── Fallback: serve the SPA shell (index.html) ──
        await configUIStaticHandler.ServeIndexHtmlAsync(ctx).ConfigureAwait(false);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when <paramref name="subPath"/> identifies a static asset
    /// that must be served without authentication.
    /// </summary>
    private static bool IsStaticAsset(string subPath)
    {
        if (subPath.StartsWith("static/", StringComparison.OrdinalIgnoreCase))
            return true;

        var ext = Path.GetExtension(subPath).ToLowerInvariant();
        return ext is ".css" or ".js" or ".ico" or ".png" or ".svg";
    }
}