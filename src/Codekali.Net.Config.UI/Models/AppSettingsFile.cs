namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Represents a discovered appsettings JSON file on disk.
/// </summary>
public sealed class AppSettingsFile
{
    /// <summary>The file name, e.g. "appsettings.Development.json".</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The absolute path on disk.</summary>
    public string FullPath { get; init; } = string.Empty;

    /// <summary>
    /// The environment this file targets, derived from the file name.
    /// E.g. "Development" from "appsettings.Development.json".
    /// Returns "Base" for plain "appsettings.json".
    /// </summary>
    public string Environment { get; init; } = string.Empty;

    /// <summary>Whether the file exists on disk at the time of discovery.</summary>
    public bool Exists { get; init; }

    /// <summary>Last write time of the file, UTC.</summary>
    public DateTimeOffset LastModified { get; init; }
}
