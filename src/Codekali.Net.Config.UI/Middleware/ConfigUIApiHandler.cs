using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Codekali.Net.Config.UI.Services;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codekali.Net.Config.UI.Middleware
{
    /// <summary>
    /// Handles all /config-ui/api/* requests. Routing is driven by a static
    /// route table built once at construction — no chain of if-statements.
    /// Each route is a (method, pattern) pair mapped to a focused handler method.
    /// </summary>
    internal sealed class ConfigUIApiHandler
    {
        private readonly IAppSettingsService _appSettings;
        private readonly IEnvironmentSwapService _envSwap;
        private readonly IBackupService _backup;

        private static readonly JsonSerializerOptions _readOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        private readonly IReadOnlyDictionary<(string Method, string Action),
            Func<HttpContext, string, Task>> _fileRoutes;

        public ConfigUIApiHandler(
            IAppSettingsService appSettingsService,
            IEnvironmentSwapService environmentSwapService,
            IBackupService backupService)
        {
            _appSettings = appSettingsService;
            _envSwap = environmentSwapService;
            _backup = backupService;

            _fileRoutes = new Dictionary<(string, string), Func<HttpContext, string, Task>>()
            {
                [("GET", "entries")] = GetEntriesAsync,
                [("GET", "raw")] = GetRawAsync,
                [("GET", "backups")] = GetBackupsAsync,
                [("GET", "value")] = GetValueAsync,
                [("POST", "entries")] = PostEntryAsync,
                [("POST", "backup")] = PostBackupAsync,
                [("POST", "array-append")] = PostArrayAppendAsync,   // ← new
                [("PUT", "entries")] = PutEntryAsync,
                [("PUT", "raw")] = PutRawAsync,
                [("DELETE", "entries")] = DeleteEntryAsync,
                [("DELETE", "array-item")] = DeleteArrayItemAsync,   // ← new
            };
        }

        public async Task HandleApiAsync(HttpContext ctx, string apiPath, string method)
        {
            if (apiPath.Equals("files", StringComparison.OrdinalIgnoreCase) && method == "GET")
            { await HandleGetFilesAsync(ctx).ConfigureAwait(false); return; }

            if (apiPath.Equals("swap", StringComparison.OrdinalIgnoreCase) && method == "POST")
            { await HandleSwapAsync(ctx).ConfigureAwait(false); return; }

            if (apiPath.Equals("diff", StringComparison.OrdinalIgnoreCase) && method == "GET")
            { await HandleDiffAsync(ctx).ConfigureAwait(false); return; }

            if (apiPath.Equals("conflicts", StringComparison.OrdinalIgnoreCase) && method == "GET")
            { await HandleConflictsAsync(ctx).ConfigureAwait(false); return; }

            if (apiPath.StartsWith("files/", StringComparison.OrdinalIgnoreCase))
            {
                var parts = apiPath.Split('/', 4);
                if (parts.Length >= 3)
                {
                    var fileName = Uri.UnescapeDataString(parts[1]);
                    var action = parts[2];
                    if (_fileRoutes.TryGetValue((method, action), out var handler))
                    { await handler(ctx, fileName).ConfigureAwait(false); return; }
                }
            }

            ctx.Response.StatusCode = 404;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx,
                new { error = $"Unknown API route: {apiPath}" }).ConfigureAwait(false);
        }

        // ── Top-level handlers ────────────────────────────────────────────────

        private async Task HandleGetFilesAsync(HttpContext ctx)
        {
            var result = await _appSettings.GetAllFilesAsync(ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task HandleSwapAsync(HttpContext ctx)
        {
            var request = await ReadBodyAsync<SwapRequest>(ctx).ConfigureAwait(false);
            if (request is null) { await BadRequestAsync(ctx, "Invalid swap request body.").ConfigureAwait(false); return; }
            var result = await _envSwap.ExecuteSwapAsync(request, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task HandleDiffAsync(HttpContext ctx)
        {
            var source = ctx.Request.Query["source"].ToString();
            var target = ctx.Request.Query["target"].ToString();
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            { await BadRequestAsync(ctx, "Both 'source' and 'target' query params are required.").ConfigureAwait(false); return; }
            var result = await _envSwap.CompareFilesAsync(source, target, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task HandleConflictsAsync(HttpContext ctx)
        {
            var target = ctx.Request.Query["target"].ToString();
            var keys = ctx.Request.Query["keys"].ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            var result = await _envSwap.FindConflictsAsync(target, keys, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        // ── File-scoped handlers ──────────────────────────────────────────────

        private async Task GetEntriesAsync(HttpContext ctx, string fileName)
        {
            var result = await _appSettings.GetEntriesAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task GetRawAsync(HttpContext ctx, string fileName)
        {
            var result = await _appSettings.GetRawJsonAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task GetBackupsAsync(HttpContext ctx, string fileName)
        {
            var result = await _backup.ListBackupsAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task GetValueAsync(HttpContext ctx, string fileName)
        {
            var keyPath = Uri.UnescapeDataString(ctx.Request.Query["key"].ToString());
            if (string.IsNullOrWhiteSpace(keyPath))
            { await BadRequestAsync(ctx, "Missing 'key' query parameter.").ConfigureAwait(false); return; }

            var rawResult = await _appSettings.GetRawJsonAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
            if (!rawResult.IsSuccess) { await RespondAsync(ctx, rawResult).ConfigureAwait(false); return; }

            var root = JsonHelper.ParseObject(rawResult.Value!);
            if (root is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx,
                    new { success = false, error = "Invalid JSON in file." }).ConfigureAwait(false);
                return;
            }

            var node = JsonHelper.GetNode(root, keyPath);
            if (node is null)
            {
                ctx.Response.StatusCode = 404;
                await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx,
                    new { success = false, error = $"Key '{keyPath}' not found." }).ConfigureAwait(false);
                return;
            }

            var plainValue = node is JsonValue jv && jv.TryGetValue<string>(out var s)
                ? s : node.ToJsonString();

            ctx.Response.StatusCode = StatusCodes.Status200OK;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx,
                new { success = true, data = plainValue }).ConfigureAwait(false);
        }

        private async Task PostEntryAsync(HttpContext ctx, string fileName)
        {
            var body = await ReadBodyAsync<EntryPayload>(ctx).ConfigureAwait(false);
            if (body is null) { await BadRequestAsync(ctx, "Invalid request body.").ConfigureAwait(false); return; }
            var result = await _appSettings.AddEntryAsync(
                fileName, body.KeyPath, body.JsonValue, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task PostBackupAsync(HttpContext ctx, string fileName)
        {
            var result = await _backup.CreateBackupAsync(fileName, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        // ── Array handlers (new) ──────────────────────────────────────────────

        private async Task PostArrayAppendAsync(HttpContext ctx, string fileName)
        {
            var body = await ReadBodyAsync<EntryPayload>(ctx).ConfigureAwait(false);
            if (body is null) { await BadRequestAsync(ctx, "Invalid request body.").ConfigureAwait(false); return; }
            var result = await _appSettings.AppendArrayItemAsync(
                fileName, body.KeyPath, body.JsonValue, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task DeleteArrayItemAsync(HttpContext ctx, string fileName)
        {
            var keyPath = Uri.UnescapeDataString(ctx.Request.Query["key"].ToString());
            var indexStr = ctx.Request.Query["index"].ToString();

            if (string.IsNullOrWhiteSpace(keyPath))
            { await BadRequestAsync(ctx, "Missing 'key' query parameter.").ConfigureAwait(false); return; }
            if (!int.TryParse(indexStr, out var index))
            { await BadRequestAsync(ctx, "Missing or invalid 'index' query parameter.").ConfigureAwait(false); return; }

            var result = await _appSettings.RemoveArrayItemAsync(
                fileName, keyPath, index, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        // ── Standard entry handlers ───────────────────────────────────────────

        private async Task PutEntryAsync(HttpContext ctx, string fileName)
        {
            var body = await ReadBodyAsync<EntryPayload>(ctx).ConfigureAwait(false);
            if (body is null) { await BadRequestAsync(ctx, "Invalid request body.").ConfigureAwait(false); return; }
            var result = await _appSettings.UpdateEntryAsync(
                fileName, body.KeyPath, body.JsonValue, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task PutRawAsync(HttpContext ctx, string fileName)
        {
            var body = await ReadBodyAsync<RawPayload>(ctx).ConfigureAwait(false);
            if (body is null) { await BadRequestAsync(ctx, "Invalid request body.").ConfigureAwait(false); return; }
            var result = await _appSettings.SaveRawJsonAsync(
                fileName, body.Content, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task DeleteEntryAsync(HttpContext ctx, string fileName)
        {
            var fullPath = ctx.Request.Path.Value ?? string.Empty;
            var entriesMarker = "/entries/";
            var markerIdx = fullPath.IndexOf(entriesMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx < 0) { await BadRequestAsync(ctx, "Missing key path in DELETE request.").ConfigureAwait(false); return; }
            var keyPath = Uri.UnescapeDataString(fullPath[(markerIdx + entriesMarker.Length)..]);
            var result = await _appSettings.DeleteEntryAsync(fileName, keyPath, ctx.RequestAborted).ConfigureAwait(false);
            await RespondAsync(ctx, result).ConfigureAwait(false);
        }

        // ── Response helpers ──────────────────────────────────────────────────

        private static async Task RespondAsync<T>(HttpContext ctx, OperationResult<T> result)
        {
            ctx.Response.StatusCode = result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx, result.IsSuccess
                ? (object)new { success = true, data = result.Value }
                : new { success = false, error = result.Error }).ConfigureAwait(false);
        }

        private static async Task RespondAsync(HttpContext ctx, OperationResult result)
        {
            ctx.Response.StatusCode = result.IsSuccess ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx, result.IsSuccess
                ? (object)new { success = true }
                : new { success = false, error = result.Error }).ConfigureAwait(false);
        }

        private static async Task BadRequestAsync(HttpContext ctx, string message)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ConfigUIMiddlewareHelpers.WriteJsonAsync(ctx,
                new { success = false, error = message }).ConfigureAwait(false);
        }

        private static async Task<T?> ReadBodyAsync<T>(HttpContext ctx)
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<T>(
                    ctx.Request.Body, _readOpts, ctx.RequestAborted).ConfigureAwait(false);
            }
            catch { return default; }
        }

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
}