using System.Text.Json;
using System.Text.Json.Nodes;
using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Internal utility methods for JSON parsing, path resolution, and value classification.
/// All methods are pure functions with no I/O side-effects.
/// </summary>
internal static class JsonHelper
{
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly string[] _sensitiveExactOrPartialMatches =
    [
        "password", "secret", "token", "apikey", "api_key",
        "connectionstring", "clientsecret", "accesskey",
        "privatekey", "signingkey"
    ];

    private static readonly string[] _sensitiveSuffixes = new[]
    {
        "password", "secret", "token", "apikey", "_key", "privatekey"
    };

    // ── Parsing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a raw JSON string into a <see cref="JsonObject"/>.
    /// Comments and trailing commas are silently accepted.
    /// Returns null if the JSON is invalid or the root is not an object.
    /// </summary>
    public static JsonObject? ParseObject(string json)
    {
        try
        {
            var node = JsonNode.Parse(json, nodeOptions: null, documentOptions: _documentOptions);
            return node as JsonObject;
        }
        catch (JsonException) { return null; }
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(_writeOptions);

    /// <summary>
    /// Validates well-formed JSON (comments and trailing commas accepted).
    /// Returns null if valid; otherwise the error message.
    /// </summary>
    public static string? Validate(string json)
    {
        try { JsonDocument.Parse(json, _documentOptions); return null; }
        catch (JsonException ex) { return ex.Message; }
    }

    // ── Path navigation ───────────────────────────────────────────────────

    /// <summary>
    /// Navigates a colon-separated path on a <see cref="JsonObject"/>.
    /// Numeric segments navigate into arrays by index (e.g. <c>Cors:Origins:0</c>).
    /// Returns null if any segment is not found.
    /// </summary>
    public static JsonNode? GetNode(JsonObject root, string path)
    {
        var segments = SplitPath(path);
        JsonNode? current = root;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(segment, out var next)) return null;
                current = next;
            }
            else if (current is JsonArray arr && int.TryParse(segment, out var idx))
            {
                if (idx < 0 || idx >= arr.Count) return null;
                current = arr[idx];
            }
            else return null;
        }

        return current;
    }

    public static bool SetNode(JsonObject root, string path, JsonNode? value)
    {
        var segments = SplitPath(path);
        JsonObject current = root;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var seg = segments[i];
            if (!current.TryGetPropertyValue(seg, out var child) || child is not JsonObject childObj)
            {
                childObj = new JsonObject();
                current[seg] = childObj;
            }
            current = childObj;
        }

        current[segments[^1]] = value;
        return true;
    }

    public static bool RemoveNode(JsonObject root, string path)
    {
        var segments = SplitPath(path);
        if (segments.Length == 1) return root.Remove(segments[0]);
        var parent = GetNode(root, string.Join(":", segments[..^1])) as JsonObject;
        return parent?.Remove(segments[^1]) ?? false;
    }

    // ── Flatten (for diff/swap) ───────────────────────────────────────────

    public static Dictionary<string, string?> Flatten(JsonObject root)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenNode(root, string.Empty, result);
        return result;
    }

    private static void FlattenNode(JsonNode? node, string prefix, Dictionary<string, string?> result)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";
                    FlattenNode(kvp.Value, key, result);
                }
                break;

            case JsonArray arr:
                // Flatten array items by index so diff shows per-item changes.
                for (int i = 0; i < arr.Count; i++)
                    FlattenNode(arr[i], $"{prefix}:{i}", result);
                break;

            default:
                result[prefix] = node?.ToJsonString();
                break;
        }
    }

    // ── Entry tree ────────────────────────────────────────────────────────

    public static List<ConfigEntry> ToEntryTree(
        JsonObject root, string sourceFile, bool maskSensitive)
    {
        return root
            .Select(kvp => BuildEntry(kvp.Key, kvp.Value, sourceFile, maskSensitive))
            .ToList();
    }

    private static ConfigEntry BuildEntry(
        string key, JsonNode? node, string sourceFile, bool maskSensitive)
    {
        // Only mask at the property-name level, not for array-index keys.
        // Also skip $schema and other metadata keys.
        var isSensitive = maskSensitive && IsSensitiveKey(key);

        return node switch
        {
            JsonObject obj => new ConfigEntry
            {
                Key = key,
                ValueType = ConfigValueType.Object,
                SourceFile = sourceFile,
                IsMasked = false,
                Children = obj.Count == 0
                    ? new List<ConfigEntry>()   // empty object — still expandable
                    : obj.Select(kvp => BuildEntry(kvp.Key, kvp.Value, sourceFile, maskSensitive))
                          .ToList()
            },

            JsonArray arr => new ConfigEntry
            {
                Key = key,
                ValueType = ConfigValueType.Array,
                SourceFile = sourceFile,
                IsMasked = isSensitive,
                RawValue = isSensitive ? null : arr.ToJsonString(),
                // Build children for each array item so the tree can expand them.
                Children = arr.Select((item, idx) =>
                    BuildArrayItemEntry(idx, item, sourceFile, maskSensitive)).ToList()
            },

            JsonValue val => new ConfigEntry
            {
                Key = key,
                RawValue = isSensitive ? null : val.ToJsonString(),
                ValueType = ClassifyScalar(val),
                IsMasked = isSensitive,
                SourceFile = sourceFile
            },

            null => new ConfigEntry
            {
                Key = key,
                RawValue = "null",
                ValueType = ConfigValueType.Null,
                SourceFile = sourceFile
            },

            _ => new ConfigEntry
            {
                Key = key,
                RawValue = isSensitive ? null : node.ToJsonString(),
                ValueType = ConfigValueType.String,
                IsMasked = isSensitive,
                SourceFile = sourceFile
            }
        };
    }

    private static ConfigEntry BuildArrayItemEntry(
        int index, JsonNode? item, string sourceFile, bool maskSensitive)
    {
        var key = index.ToString();

        return item switch
        {
            JsonObject obj => new ConfigEntry
            {
                Key = key,
                ArrayIndex = index,
                ValueType = ConfigValueType.Object,
                SourceFile = sourceFile,
                Children = obj.Select(kvp =>
                    BuildEntry(kvp.Key, kvp.Value, sourceFile, maskSensitive)).ToList()
            },

            JsonArray inner => new ConfigEntry
            {
                Key = key,
                ArrayIndex = index,
                ValueType = ConfigValueType.Array,
                SourceFile = sourceFile,
                RawValue = inner.ToJsonString(),
                Children = inner.Select((it, i) =>
                    BuildArrayItemEntry(i, it, sourceFile, maskSensitive)).ToList()
            },

            JsonValue val => new ConfigEntry
            {
                Key = key,
                ArrayIndex = index,
                RawValue = val.ToJsonString(),
                ValueType = ConfigValueType.ArrayItem,
                SourceFile = sourceFile
            },

            null => new ConfigEntry
            {
                Key = key,
                ArrayIndex = index,
                RawValue = "null",
                ValueType = ConfigValueType.ArrayItem,
                SourceFile = sourceFile
            },

            _ => new ConfigEntry
            {
                Key = key,
                ArrayIndex = index,
                RawValue = item.ToJsonString(),
                ValueType = ConfigValueType.ArrayItem,
                SourceFile = sourceFile
            }
        };
    }

    // ── Sensitivity check ─────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the key name suggests a sensitive value.
    /// Uses whole-word matching (exact or suffix-based) to avoid false positives.
    /// Examples: "password", "ApiKey", "my_secret", "JwtToken" → true
    /// Examples: "$schema", "AllowedOrigins", "PublicKey" → false
    /// </summary>
    public static bool IsSensitiveKey(string key)
    {
        // Strip leading $ (e.g., $schema is not sensitive)
        var lower = key.TrimStart('$').ToLowerInvariant();

        if (string.IsNullOrEmpty(lower))
            return false;

        // Check exact matches first (most specific, fastest)
        if (Array.Exists(_sensitiveExactOrPartialMatches, lower.Contains))
            return true;

        // Check suffix matches (general patterns like "*Password", "*_key")
        foreach (var suffix in _sensitiveSuffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    public static string[] SplitPath(string path) =>
        path.Split(':', StringSplitOptions.RemoveEmptyEntries);

    private static ConfigValueType ClassifyScalar(JsonValue val)
    {
        if (val.TryGetValue<bool>(out _)) return ConfigValueType.Boolean;
        if (val.TryGetValue<double>(out _)) return ConfigValueType.Number;
        if (val.TryGetValue<string>(out _)) return ConfigValueType.String;
        return ConfigValueType.String;
    }
}
