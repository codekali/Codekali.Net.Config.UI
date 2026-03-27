namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Low-level repository abstraction for reading and writing appsettings files.
/// All file I/O in the library funnels through this interface so that the higher-level
/// services remain fully testable without touching the real file system.
/// </summary>
public interface IConfigFileRepository
{
    /// <summary>Returns all <c>appsettings*.json</c> file paths under the configured directory.</summary>
    IEnumerable<string> DiscoverFiles();

    /// <summary>Reads the full text content of the file at <paramref name="fullPath"/>.</summary>
    Task<string> ReadAllTextAsync(string fullPath, CancellationToken ct = default);

    /// <summary>Writes <paramref name="content"/> to the file at <paramref name="fullPath"/>, creating it if necessary.</summary>
    Task WriteAllTextAsync(string fullPath, string content, CancellationToken ct = default);

    /// <summary>Copies the file at <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.</summary>
    Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken ct = default);

    /// <summary>Returns whether the file at <paramref name="fullPath"/> exists.</summary>
    bool FileExists(string fullPath);

    /// <summary>Returns the last-write time of the file, or <see cref="DateTimeOffset.MinValue"/> if the file does not exist.</summary>
    DateTimeOffset GetLastWriteTime(string fullPath);

    /// <summary>
    /// Returns the full path for a file given its name, resolved against the configured directory.
    /// </summary>
    string ResolvePath(string fileName);

    /// <summary>Returns all <c>*.bak</c> paths that match the given appsettings file name.</summary>
    IEnumerable<string> DiscoverBackups(string fullPath);
}
