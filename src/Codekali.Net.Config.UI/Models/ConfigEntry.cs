namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Represents a single configuration entry (key-value pair) within an appsettings file.
/// A key may be a simple scalar value or a JSON object/array.
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>The dot-notation path of the key, e.g. "ConnectionStrings:Default".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The raw JSON value for this key (string, number, object, array, or null literal).</summary>
    public string? RawValue { get; set; }

    /// <summary>The inferred value type of this entry.</summary>
    public ConfigValueType ValueType { get; init; } = ConfigValueType.String;

    /// <summary>Whether this key's value is masked because it appears to contain sensitive data.</summary>
    public bool IsMasked { get; set; }

    /// <summary>Child entries when <see cref="ValueType"/> is <see cref="ConfigValueType.Object"/>.</summary>
    public List<ConfigEntry> Children { get; init; } = [];

    /// <summary>The name of the appsettings file this entry belongs to, e.g. "appsettings.Development.json".</summary>
    public string SourceFile { get; init; } = string.Empty;
}

/// <summary>Describes the JSON value type for a <see cref="ConfigEntry"/>.</summary>
public enum ConfigValueType
{
    /// <summary>A plain string or scalar value.</summary>
    String,
    /// <summary>A numeric value.</summary>
    Number,
    /// <summary>A boolean value.</summary>
    Boolean,
    /// <summary>A null literal.</summary>
    Null,
    /// <summary>A JSON object containing child entries.</summary>
    Object,
    /// <summary>A JSON array.</summary>
    Array
}
