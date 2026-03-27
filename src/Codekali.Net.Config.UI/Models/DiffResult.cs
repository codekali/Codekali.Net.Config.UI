namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// The result of comparing two appsettings files side by side.
/// </summary>
public sealed class DiffResult
{
    /// <summary>The source file name used in the comparison.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>The target file name used in the comparison.</summary>
    public string TargetFile { get; init; } = string.Empty;

    /// <summary>Keys that exist only in the source file.</summary>
    public List<string> OnlyInSource { get; init; } = [];

    /// <summary>Keys that exist only in the target file.</summary>
    public List<string> OnlyInTarget { get; init; } = [];

    /// <summary>Keys that exist in both files but have different values.</summary>
    public List<DiffEntry> ValueDifferences { get; init; } = [];

    /// <summary>Keys that are identical in both files.</summary>
    public List<string> Identical { get; init; } = [];
}

/// <summary>
/// Describes a single key whose value differs between two appsettings files.
/// </summary>
public sealed class DiffEntry
{
    /// <summary>The dot-notation key path.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The value in the source file.</summary>
    public string? SourceValue { get; init; }

    /// <summary>The value in the target file.</summary>
    public string? TargetValue { get; init; }
}
