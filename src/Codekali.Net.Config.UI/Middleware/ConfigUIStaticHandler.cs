using Codekali.Net.Config.UI.Models;
using Microsoft.AspNetCore.Http;
using System.Text;

namespace Codekali.Net.Config.UI.Middleware
{
    internal sealed class ConfigUIStaticHandler(ConfigUIOptions _options)
    {
        public async Task ServeIndexHtmlAsync(HttpContext ctx)
        {
            var html = ReadEmbeddedResource("Index.html");
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

        public static async Task ServeEmbeddedResourceAsync(HttpContext ctx, string subPath)
        {
            var content = ReadEmbeddedResource(subPath);
            if (content is null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = ConfigUIMiddlewareHelpers.GetContentType(subPath);
            await ctx.Response.WriteAsync(content, ctx.RequestAborted).ConfigureAwait(false);
        }

        public static string? ReadEmbeddedResource(string subPath)
        {
            var assembly = typeof(ConfigUIStaticHandler).Assembly;

            // In Release builds the minified variants are embedded alongside the
            // originals. Prefer them by inserting ".min" before the extension.
            var minPath = ToMinPath(subPath);
            var suffix = (minPath ?? subPath).Replace('/', '.').Replace('\\', '.');

            var resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

            // Fall back to unminified if the .min variant was not embedded
            // (e.g. Debug build or minification was skipped).
            if (resourceName is null && minPath is not null)
            {
                suffix = subPath.Replace('/', '.').Replace('\\', '.');
                resourceName = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            }

            if (resourceName is null) return null;

            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string? ToMinPath(string subPath)
        {
            var ext = Path.GetExtension(subPath);
            if (ext is not ".css" and not ".js") return null;
            return subPath[..^ext.Length] + ".min" + ext;
        }
    }
}
