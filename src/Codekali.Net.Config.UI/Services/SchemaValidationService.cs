using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Validates appsettings JSON against a JSON Schema draft-07 subset.
/// Supported keywords: type, required, properties, additionalProperties,
/// minLength, maxLength, pattern, minimum, maximum, enum, minItems, maxItems.
/// </summary>
internal sealed class SchemaValidationService : ISchemaValidationService
{
    private readonly ConfigUIOptions _options;
    private readonly ILogger<SchemaValidationService> _logger;

    // Lazily loaded and cached schema — null until first access.
    private string? _schemaJson;
    private JsonObject? _schemaRoot;
    private bool _loaded;

    public SchemaValidationService(ConfigUIOptions options, ILogger<SchemaValidationService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsSchemaConfigured =>
        !string.IsNullOrWhiteSpace(_options.SchemaJson) ||
        !string.IsNullOrWhiteSpace(_options.SchemaPath);

    public string? GetSchemaJson()
    {
        EnsureLoaded();
        return _schemaJson;
    }

    public async Task<IReadOnlyList<SchemaViolation>> ValidateAsync(
        string rawJson, CancellationToken ct = default)
    {
        if (!IsSchemaConfigured) return [];

        await EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (_schemaRoot is null) return [];

        var doc = JsonHelper.ParseObject(rawJson);
        if (doc is null)
            return [new SchemaViolation { KeyPath = "$", Message = "Document is not a valid JSON object.", Keyword = "type", Severity = "error" }];

        var violations = new List<SchemaViolation>();
        ValidateObject(doc, _schemaRoot, "$", violations);
        return violations;
    }

    // ── Schema loading ────────────────────────────────────────────────────

    private void EnsureLoaded() => EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        _loaded = true;

        // Inline JSON takes precedence over file path.
        if (!string.IsNullOrWhiteSpace(_options.SchemaJson))
        {
            _schemaJson = _options.SchemaJson;
        }
        else if (!string.IsNullOrWhiteSpace(_options.SchemaPath))
        {
            var resolved = Path.IsPathRooted(_options.SchemaPath)
                ? _options.SchemaPath
                : Path.Combine(_options.ConfigDirectory ?? Directory.GetCurrentDirectory(), _options.SchemaPath);

            if (!File.Exists(resolved))
            {
                _logger.LogWarning("[ConfigUI Schema] Schema file not found: {Path}", resolved);
                return;
            }
            _schemaJson = await File.ReadAllTextAsync(resolved, ct).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(_schemaJson)) return;

        try
        {
            _schemaRoot = JsonNode.Parse(_schemaJson) as JsonObject;
            if (_schemaRoot is null)
                _logger.LogWarning("[ConfigUI Schema] Schema root is not a JSON object.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[ConfigUI Schema] Failed to parse schema JSON.");
            _schemaJson = null;
        }
    }

    // ── Validation walker ─────────────────────────────────────────────────

    private static void ValidateObject(
        JsonObject doc, JsonObject schema, string path, List<SchemaViolation> violations)
    {
        // ── required ──────────────────────────────────────────────────────
        if (schema["required"] is JsonArray required)
        {
            foreach (var item in required)
            {
                var key = item?.GetValue<string>();
                if (key is null) continue;
                if (!doc.ContainsKey(key))
                    violations.Add(new SchemaViolation
                    {
                        KeyPath = $"{path}:{key}".TrimStart('$').TrimStart(':'),
                        Message = $"Required key '{key}' is missing.",
                        Keyword = "required",
                        Severity = "error"
                    });
            }
        }

        // ── properties ────────────────────────────────────────────────────
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (key, propSchema) in properties)
            {
                if (propSchema is not JsonObject propObj) continue;
                var childPath = path == "$" ? key : $"{path}:{key}";

                if (!doc.TryGetPropertyValue(key, out var value) || value is null)
                    continue; // missing optional — required check above handles it

                ValidateNode(value, propObj, childPath, violations);
            }
        }

        // ── additionalProperties: false ───────────────────────────────────
        if (schema["additionalProperties"] is JsonValue apVal &&
            apVal.TryGetValue<bool>(out var apBool) && !apBool)
        {
            if (schema["properties"] is JsonObject definedProps)
            {
                foreach (var (key, _) in doc)
                {
                    if (!definedProps.ContainsKey(key))
                        violations.Add(new SchemaViolation
                        {
                            KeyPath = path == "$" ? key : $"{path}:{key}",
                            Message = $"Additional property '{key}' is not allowed.",
                            Keyword = "additionalProperties",
                            Severity = "warning"
                        });
                }
            }
        }
    }

    private static void ValidateNode(
        JsonNode node, JsonObject schema, string path, List<SchemaViolation> violations)
    {
        // ── type ──────────────────────────────────────────────────────────
        if (schema["type"] is JsonValue typeVal && typeVal.TryGetValue<string>(out var expectedType))
        {
            var actualType = GetJsonType(node);
            if (!TypeMatches(actualType, expectedType))
            {
                violations.Add(new SchemaViolation
                {
                    KeyPath = path,
                    Message = $"Expected type '{expectedType}' but got '{actualType}'.",
                    Keyword = "type",
                    Severity = "error"
                });
                return; // further checks on mismatched type are noisy
            }
        }

        switch (node)
        {
            case JsonObject obj:
                ValidateObject(obj, schema, path, violations);
                break;

            case JsonArray arr:
                ValidateArray(arr, schema, path, violations);
                break;

            case JsonValue val:
                ValidateScalar(val, schema, path, violations);
                break;
        }
    }

    private static void ValidateScalar(
        JsonValue val, JsonObject schema, string path, List<SchemaViolation> violations)
    {
        // ── enum ──────────────────────────────────────────────────────────
        if (schema["enum"] is JsonArray enumArr)
        {
            var raw = val.ToJsonString();
            var match = enumArr.Any(e => e?.ToJsonString() == raw);
            if (!match)
            {
                var allowed = string.Join(", ", enumArr.Select(e => e?.ToJsonString() ?? "null"));
                violations.Add(new SchemaViolation
                {
                    KeyPath = path,
                    Message = $"Value must be one of: {allowed}.",
                    Keyword = "enum",
                    Severity = "error"
                });
            }
        }

        // ── string keywords ───────────────────────────────────────────────
        if (val.TryGetValue<string>(out var str))
        {
            if (schema["minLength"] is JsonValue minLenVal && minLenVal.TryGetValue<int>(out var minLen) && str.Length < minLen)
                violations.Add(new SchemaViolation { KeyPath = path, Message = $"Must be at least {minLen} character(s) long.", Keyword = "minLength", Severity = "error" });

            if (schema["maxLength"] is JsonValue maxLenVal && maxLenVal.TryGetValue<int>(out var maxLen) && str.Length > maxLen)
                violations.Add(new SchemaViolation { KeyPath = path, Message = $"Must be at most {maxLen} character(s) long.", Keyword = "maxLength", Severity = "error" });

            if (schema["pattern"] is JsonValue patVal && patVal.TryGetValue<string>(out var pattern))
            {
                try
                {
                    if (!Regex.IsMatch(str, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                        violations.Add(new SchemaViolation { KeyPath = path, Message = $"Value does not match required pattern: {pattern}.", Keyword = "pattern", Severity = "error" });
                }
                catch (RegexMatchTimeoutException)
                {
                    violations.Add(new SchemaViolation { KeyPath = path, Message = "Pattern validation timed out.", Keyword = "pattern", Severity = "warning" });
                }
            }
        }

        // ── numeric keywords ──────────────────────────────────────────────
        if (val.TryGetValue<double>(out var num))
        {
            if (schema["minimum"] is JsonValue minVal && minVal.TryGetValue<double>(out var min) && num < min)
                violations.Add(new SchemaViolation { KeyPath = path, Message = $"Value must be >= {min}.", Keyword = "minimum", Severity = "error" });

            if (schema["maximum"] is JsonValue maxVal && maxVal.TryGetValue<double>(out var max) && num > max)
                violations.Add(new SchemaViolation { KeyPath = path, Message = $"Value must be <= {max}.", Keyword = "maximum", Severity = "error" });
        }
    }

    private static void ValidateArray(
        JsonArray arr, JsonObject schema, string path, List<SchemaViolation> violations)
    {
        if (schema["minItems"] is JsonValue minVal && minVal.TryGetValue<int>(out var min) && arr.Count < min)
            violations.Add(new SchemaViolation { KeyPath = path, Message = $"Array must have at least {min} item(s).", Keyword = "minItems", Severity = "error" });

        if (schema["maxItems"] is JsonValue maxVal && maxVal.TryGetValue<int>(out var max) && arr.Count > max)
            violations.Add(new SchemaViolation { KeyPath = path, Message = $"Array must have at most {max} item(s).", Keyword = "maxItems", Severity = "error" });

        if (schema["items"] is JsonObject itemSchema)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] is not null)
                    ValidateNode(arr[i]!, itemSchema, $"{path}:{i}", violations);
            }
        }
    }

    // ── Type helpers ──────────────────────────────────────────────────────

    private static string GetJsonType(JsonNode node) => node switch
    {
        JsonObject => "object",
        JsonArray => "array",
        JsonValue val when val.TryGetValue<bool>(out _) => "boolean",
        JsonValue val when val.TryGetValue<long>(out _) || val.TryGetValue<double>(out _) => "number",
        JsonValue val when val.TryGetValue<string>(out _) => "string",
        _ => "null"
    };

    private static bool TypeMatches(string actual, string expected) =>
        expected switch
        {
            "integer" => actual == "number", // JSON has no distinct integer type
            _ => actual == expected
        };
}