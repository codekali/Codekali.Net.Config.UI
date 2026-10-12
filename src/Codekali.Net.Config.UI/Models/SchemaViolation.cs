namespace Codekali.Net.Config.UI.Models;

/// <summary>A single schema validation violation against an appsettings key.</summary>
public sealed class SchemaViolation
{
    /// <summary>The colon-separated key path where the violation occurred.</summary>
    public string KeyPath { get; init; } = string.Empty;

    /// <summary>Human-readable description of the constraint that was violated.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The schema keyword that was violated, e.g. "required", "type", "pattern".</summary>
    public string Keyword { get; init; } = string.Empty;

    /// <summary>Severity: "error" blocks saving; "warning" is advisory only.</summary>
    public string Severity { get; init; } = "error";
}