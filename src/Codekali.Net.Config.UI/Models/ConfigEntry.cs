namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Represents a single configuration entry (key-value pair) within an appsettings file.
/// A key may be a simple scalar value or a JSON object/array.
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>The key name at this level (not the full path).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// For array items: the 0-based index within the parent array.
    /// Null for object properties.
    /// </summary>
    public int? ArrayIndex { get; set; }

    /// <summary>The raw JSON-encoded value for scalar/array/null nodes.</summary>
    public string? RawValue { get; set; }

    /// <summary>The JSON value type.</summary>
    public ConfigValueType ValueType { get; set; }

    /// <summary>The file this entry belongs to.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>True when the value is masked for security.</summary>
    public bool IsMasked { get; set; }

    /// <summary>
    /// Child entries for Object and Array nodes.
    /// For arrays: each child represents one item at its index.
    /// </summary>
    public List<ConfigEntry>? Children { get; set; }
}

/// <summary>Discriminates the JSON value type of a <see cref="ConfigEntry"/>.</summary>
public enum ConfigValueType
{
    String,
    Number,
    Boolean,
    Null,
    Object,
    /// <summary>A JSON array — children hold the individual items.</summary>
    Array,
    /// <summary>A single item inside a JSON array.</summary>
    ArrayItem,
}
