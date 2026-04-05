using Codekali.Net.Config.UI.Models;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace Codekali.Net.Config.UI.Middleware
{
    internal static class ConfigUIMiddlewareHelpers
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            // Serialize enums as strings so the browser receives "object", "string" etc.
            // instead of integer ordinals (0, 1, 2 ...) which the JS cannot pattern-match.
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public static bool IsAllowedEnvironment(string? environmentName, ConfigUIOptions options)
        {
            if (options.AllowedEnvironments.Contains("*")) return true;
            return environmentName is not null &&
                   options.AllowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsAuthorised(HttpRequest request, ConfigUIOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.AccessToken)) return true;

            var headerToken = request.Headers["X-Config-Token"].ToString();
            if (headerToken == options.AccessToken) return true;

            var queryToken = request.Query["token"].ToString();
            return queryToken == options.AccessToken;
        }

        public static bool IsMutationRequest(string method) =>
            method is "POST" or "PUT" or "DELETE" or "PATCH";

        public static async Task WriteJsonAsync(HttpContext ctx, object value)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(value, _jsonOpts), ctx.RequestAborted)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Returns true when <paramref name="subPath"/> identifies a static asset
        /// that must be served without authentication.
        /// </summary>
        public static bool IsStaticAsset(string subPath)
        {
            if (subPath.StartsWith("static/", StringComparison.OrdinalIgnoreCase))
                return true;

            var ext = Path.GetExtension(subPath).ToLowerInvariant();
            return ext is ".css" or ".js" or ".ico" or ".png" or ".svg";
        }

        public static string GetContentType(string path)
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
    }
}
