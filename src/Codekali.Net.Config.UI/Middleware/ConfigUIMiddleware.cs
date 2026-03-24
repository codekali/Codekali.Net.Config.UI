using System.Reflection;
using System.Text;
using System.Text.Json;
using Codekali.Net.Config.UI.Interfaces;
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
public sealed class ConfigUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConfigUIOptions _options;
    private readonly ILogger<ConfigUIMiddleware> _logger;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public ConfigUIMiddleware(
        RequestDelegate next,
        ConfigUIOptions options,
        ILogger<ConfigUIMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    /// <summary>Invokes the middleware for each HTTP request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(_options.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // ── Environment guard ──────────────────────────────────────────────
        var env = context.RequestServices.GetService<IHostEnvironment>();
        if (!IsAllowedEnvironment(env?.EnvironmentName))
        {
            context.Response.StatusCode = 404;
            return;
        }

        // ── Token guard ────────────────────────────────────────────────────
        if (!IsAuthorised(context.Request))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized — supply the correct access token.", context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        // ── Read-only guard for mutations ──────────────────────────────────
        var subPath = path[_options.PathPrefix.Length..].TrimStart('/');

        if (_options.ReadOnly && IsMutationRequest(context.Request.Method, subPath))
        {
            context.Response.StatusCode = 403;
            await WriteJsonAsync(context, new { error = "Config UI is in read-only mode." })
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
            await ServeEmbeddedResourceAsync(ctx, subPath).ConfigureAwait(false);
            return;
        }

        // ── API routes (/config-ui/api/*) ──
        if (subPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            await HandleApiAsync(ctx, subPath[4..], method).ConfigureAwait(false);
            return;
        }

        // ── Fallback: serve the SPA shell ──
        await ServeIndexHtmlAsync(ctx).ConfigureAwait(false);
    }

    private async Task HandleApiAsync(HttpContext ctx, string apiPath, string method)
    {
        var svc = ctx.RequestServices.GetRequiredService<IAppSettingsService>();
        var swapSvc = ctx.RequestServices.GetRequiredService<IEnvironmentSwapService>();
        var backupSvc = ctx.RequestServices.GetRequiredService<IBackupService>();

        // GET  api/files
        if (apiPath.Equals("files", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var result = await svc.GetAllFilesAsync(ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
            return;
        }

        // GET  api/files/{fileName}/entries
        if (method == "GET" && apiPath.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = apiPath.Split('/');
            if (parts.Length >= 3 && parts[2].Equals("entries", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var result = await svc.GetEntriesAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }

            if (parts.Length >= 3 && parts[2].Equals("raw", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var result = await svc.GetRawJsonAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }

            if (parts.Length >= 3 && parts[2].Equals("backups", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var result = await backupSvc.ListBackupsAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }
        }

        // POST api/files/{fileName}/entries  — Add
        if (method == "POST" && apiPath.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = apiPath.Split('/');
            if (parts.Length >= 3 && parts[2].Equals("entries", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var body = await ReadBodyAsync<EntryPayload>(ctx).ConfigureAwait(false);
                if (body is null) { await BadRequest(ctx, "Invalid request body.").ConfigureAwait(false); return; }
                var result = await svc.AddEntryAsync(fileName, body.KeyPath, body.JsonValue, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }

            // PUT api/files/{fileName}/raw  — Save raw JSON
            if (parts.Length >= 3 && parts[2].Equals("raw", StringComparison.OrdinalIgnoreCase) && method == "PUT")
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var body = await ReadBodyAsync<RawPayload>(ctx).ConfigureAwait(false);
                if (body is null) { await BadRequest(ctx, "Invalid request body.").ConfigureAwait(false); return; }
                var result = await svc.SaveRawJsonAsync(fileName, body.Content, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }
        }

        // PUT api/files/{fileName}/entries  — Update
        if (method == "PUT" && apiPath.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = apiPath.Split('/');
            if (parts.Length >= 3 && parts[2].Equals("entries", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var body = await ReadBodyAsync<EntryPayload>(ctx).ConfigureAwait(false);
                if (body is null) { await BadRequest(ctx, "Invalid request body.").ConfigureAwait(false); return; }
                var result = await svc.UpdateEntryAsync(fileName, body.KeyPath, body.JsonValue, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }

            // PUT api/files/{fileName}/raw
            if (parts.Length >= 3 && parts[2].Equals("raw", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var body = await ReadBodyAsync<RawPayload>(ctx).ConfigureAwait(false);
                if (body is null) { await BadRequest(ctx, "Invalid request body.").ConfigureAwait(false); return; }
                var result = await svc.SaveRawJsonAsync(fileName, body.Content, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }
        }

        // DELETE api/files/{fileName}/entries/{keyPath}
        if (method == "DELETE" && apiPath.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = apiPath.Split('/', 4);
            if (parts.Length >= 4 && parts[2].Equals("entries", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = Uri.UnescapeDataString(parts[1]);
                var keyPath = Uri.UnescapeDataString(parts[3]);
                var result = await svc.DeleteEntryAsync(fileName, keyPath, ctx.RequestAborted).ConfigureAwait(false);
                await RespondAsync(ctx, result).ConfigureAwait(false);
                return;
            }
        }

        // POST api/swap
        if (apiPath.Equals("swap", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var request = await ReadBodyAsync<SwapRequest>(ctx).ConfigureAwait(false);
            if (request is null) { await BadRequest(ctx, "Invalid swap request body.").ConfigureAwait(false); return; }
            var result = await swapSvc.ExecuteSwapAsync(request, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
            return;
        }

        // GET  api/diff?source=X&target=Y
        if (apiPath.Equals("diff", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var source = ctx.Request.Query["source"].ToString();
            var target = ctx.Request.Query["target"].ToString();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                await BadRequest(ctx, "Both 'source' and 'target' query params are required.").ConfigureAwait(false);
                return;
            }
            var result = await swapSvc.CompareFilesAsync(source, target, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
            return;
        }

        // GET api/conflicts?target=X&keys=a,b,c
        if (apiPath.Equals("conflicts", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            var target = ctx.Request.Query["target"].ToString();
            var keys = ctx.Request.Query["keys"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            var result = await swapSvc.FindConflictsAsync(target, keys, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
            return;
        }

        ctx.Response.StatusCode = 404;
        await WriteJsonAsync(ctx, new { error = $"Unknown API route: {apiPath}" }).ConfigureAwait(false);
    }

    // ── Static resource serving ──────────────────────────────────────────────

    private async Task ServeIndexHtmlAsync(HttpContext ctx)
    {
        var html = ReadEmbeddedResource("UI.wwwroot.index.html");
        if (html is null)
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("Config UI assets not found. Ensure the library was built correctly.")
                .ConfigureAwait(false);
            return;
        }

        // Inject the path prefix so the JS knows where to call the API
        html = html.Replace("__CONFIG_UI_PATH_PREFIX__", _options.PathPrefix);
        html = html.Replace("__CONFIG_UI_READONLY__", _options.ReadOnly.ToString().ToLower());

        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync(html, ctx.RequestAborted).ConfigureAwait(false);
    }

    private async Task ServeEmbeddedResourceAsync(HttpContext ctx, string subPath)
    {
        var resourceKey = subPath.Replace('/', '.').Replace('-', '_');
        var content = ReadEmbeddedResource($"UI.wwwroot.{resourceKey}");

        if (content is null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }

        ctx.Response.ContentType = GetContentType(subPath);
        await ctx.Response.WriteAsync(content, ctx.RequestAborted).ConfigureAwait(false);
    }

    private static string? ReadEmbeddedResource(string resourceSuffix)
    {
        var assembly = typeof(ConfigUIMiddleware).Assembly;
        var baseName = assembly.GetName().Name;
        var resourceName = $"{baseName}.{resourceSuffix}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsAllowedEnvironment(string? environmentName)
    {
        if (_options.AllowedEnvironments.Contains("*")) return true;
        return environmentName is not null &&
               _options.AllowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsAuthorised(HttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken)) return true;

        var headerToken = request.Headers["X-Config-Token"].ToString();
        if (headerToken == _options.AccessToken) return true;

        var queryToken = request.Query["token"].ToString();
        return queryToken == _options.AccessToken;
    }

    private static bool IsMutationRequest(string method, string subPath) =>
        method is "POST" or "PUT" or "DELETE" or "PATCH";

    private static async Task WriteJsonAsync(HttpContext ctx, object value)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(value, _jsonOpts), ctx.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task RespondAsync<T>(HttpContext ctx, OperationResult<T> result)
    {
        if (result.IsSuccess)
        {
            ctx.Response.StatusCode = 200;
            await WriteJsonAsync(ctx, new { success = true, data = result.Value }).ConfigureAwait(false);
        }
        else
        {
            ctx.Response.StatusCode = 400;
            await WriteJsonAsync(ctx, new { success = false, error = result.Error }).ConfigureAwait(false);
        }
    }

    private static async Task RespondAsync(HttpContext ctx, OperationResult result)
    {
        if (result.IsSuccess)
        {
            ctx.Response.StatusCode = 200;
            await WriteJsonAsync(ctx, new { success = true }).ConfigureAwait(false);
        }
        else
        {
            ctx.Response.StatusCode = 400;
            await WriteJsonAsync(ctx, new { success = false, error = result.Error }).ConfigureAwait(false);
        }
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext ctx)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                ctx.Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                ctx.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            return default;
        }
    }

    private static async Task BadRequest(HttpContext ctx, string message)
    {
        ctx.Response.StatusCode = 400;
        await WriteJsonAsync(ctx, new { success = false, error = message }).ConfigureAwait(false);
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".css" => "text/css; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".ico" => "image/x-icon",
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            _ => "text/plain; charset=utf-8"
        };
    }

    // ── Request body DTOs (private, middleware-layer only) ───────────────────

    private sealed class EntryPayload
    {
        public string KeyPath { get; set; } = string.Empty;
        public string JsonValue { get; set; } = string.Empty;
    }

    private sealed class RawPayload
    {
        public string Content { get; set; } = string.Empty;
    }
}
