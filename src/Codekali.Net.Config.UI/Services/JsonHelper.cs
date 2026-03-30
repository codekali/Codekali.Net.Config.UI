using Codekali.Net.Config.UI.Extensions;
using Codekali.Net.Config.UI.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Internal utility methods for JSON parsing, path resolution, and value classification.
/// All methods are pure functions with no I/O side-effects.
/// </summary>
internal static class JsonHelper
{
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Shared document options that instruct the parser to skip (not reject)
    /// <c>//</c> and <c>/* */</c> comments. Applied to every parse call so that
    /// appsettings files containing developer comments are handled correctly on
    /// the read path (tree view, diff, swap, existence checks).
    ///
    /// Note: comments are stripped by System.Text.Json at parse time — they are
    /// not round-tripped. The Newtonsoft-based write path in
    /// <see cref="JsonCommentPreservingWriter"/> is responsible for preserving
    /// comments across write operations.
    /// </summary>
    private static readonly JsonDocumentOptions _documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true   // defensive — tolerates minor formatting variations
    };

    /// <summary>
    /// Parses a raw JSON string into a <see cref="JsonObject"/>.
    /// Comments (<c>//</c> and <c>/* */</c>) are silently skipped.
    /// Returns null if the JSON is invalid or the root is not an object.
    /// </summary>
    public static JsonObject? ParseObject(string json)
    {
        try
        {
            var node = JsonNode.Parse(json, nodeOptions: null, documentOptions: _documentOptions);
            return node as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serialises a <see cref="JsonNode"/> back to an indented JSON string.
    /// </summary>
    public static string Serialize(JsonNode node) =>
        node.ToJsonString(_writeOptions);

    /// <summary>
    /// Validates that <paramref name="json"/> is well-formed JSON.
    /// Comments and trailing commas are accepted.
    /// Returns the error message if invalid, or null if valid.
    /// </summary>
    public static string? Validate(string json)
    {
        try
        {
            JsonDocument.Parse(json, _documentOptions);
            return null;
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Navigates a colon-separated <paramref name="path"/> on a <see cref="JsonObject"/>
    /// and returns the value node, or null if not found.
    /// E.g. "ConnectionStrings:Default" → root["ConnectionStrings"]["Default"].
    /// </summary>
    public static JsonNode? GetNode(JsonObject root, string path)
    {
        var segments = SplitPath(path);
        JsonNode? current = root;

        foreach (var segment in segments)
        {
            if (current is JsonObject obj && obj.TryGetPropertyValue(segment, out var next))
                current = next;
            else
                return null;
        }

        return current;
    }

    /// <summary>
    /// Sets the value at a colon-separated <paramref name="path"/>, creating
    /// intermediate objects as needed.
    /// </summary>
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

    /// <summary>
    /// Removes the node at the given colon-separated path.
    /// Returns true if the key existed and was removed, false otherwise.
    /// </summary>
    public static bool RemoveNode(JsonObject root, string path)
    {
        var segments = SplitPath(path);

        if (segments.Length == 1)
            return root.Remove(segments[0]);

        var parent = GetNode(root, string.Join(":", segments[..^1])) as JsonObject;
        return parent?.Remove(segments[^1]) ?? false;
    }

    /// <summary>
    /// Flattens a <see cref="JsonObject"/> into a dictionary of
    /// colon-separated keys → raw JSON value strings.
    /// </summary>
    public static Dictionary<string, string?> Flatten(JsonObject root)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        FlattenNode(root, string.Empty, result);
        return result;
    }

    /// <summary>
    /// Converts a <see cref="JsonObject"/> into a tree of <see cref="ConfigEntry"/> objects.
    /// </summary>
    public static List<ConfigEntry> ToEntryTree(JsonObject root, string sourceFile, bool maskSensitive)
    {
        return root
            .Select(kvp => BuildEntry(kvp.Key, kvp.Value, sourceFile, maskSensitive))
            .ToList();
    }

    /// <summary>
    /// Returns true if the key name suggests it holds a sensitive value.
    /// </summary>
    public static bool IsSensitiveKey(string key)
    {
        var lower = key.ToLowerInvariant();
        return lower.Contains("password") ||
               lower.Contains("secret") ||
               lower.Contains("token") ||
               lower.Contains("apikey") ||
               lower.Contains("api_key") ||
               lower.Contains("connectionstring");
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private static string[] SplitPath(string path) =>
        path.Split(':', StringSplitOptions.RemoveEmptyEntries);

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
                result[prefix] = arr.ToJsonString();
                break;

            default:
                result[prefix] = node?.ToJsonString();
                break;
        }
    }

    private static ConfigEntry BuildEntry(
        string key, JsonNode? node, string sourceFile, bool maskSensitive)
    {
        var isSensitive = maskSensitive && IsSensitiveKey(key);

        return node switch
        {
            JsonObject obj => new ConfigEntry
            {
                Key = key,
                ValueType = ConfigValueType.Object,
                SourceFile = sourceFile,
                IsMasked = false,
                Children = obj.Select(kvp =>
                    BuildEntry(kvp.Key, kvp.Value, sourceFile, maskSensitive)).ToList()
            },
            JsonArray arr => new ConfigEntry
            {
                Key = key,
                RawValue = isSensitive ? null : arr.ToJsonString(),
                ValueType = ConfigValueType.Array,
                IsMasked = isSensitive,
                SourceFile = sourceFile
            },
            JsonValue val => new ConfigEntry
            {
                Key = key,
                RawValue = isSensitive ? null : val.ToJsonString(),
                ValueType = ClassifyValue(val),
                IsMasked = isSensitive,
                SourceFile = sourceFile
            },
            null => new ConfigEntry
            {
                Key = key,
                RawValue = "null",
                ValueType = ConfigValueType.Null,
                IsMasked = false,
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

    private static ConfigValueType ClassifyValue(JsonValue val)
    {
        if (val.TryGetValue<bool>(out _)) return ConfigValueType.Boolean;
        if (val.TryGetValue<double>(out _)) return ConfigValueType.Number;
        if (val.TryGetValue<string>(out _)) return ConfigValueType.String;
        return ConfigValueType.String;
    }
}