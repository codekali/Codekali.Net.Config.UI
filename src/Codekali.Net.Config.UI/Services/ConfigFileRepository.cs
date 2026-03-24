using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Concrete file system implementation of <see cref="IConfigFileRepository"/>.
/// This is the only class in the library that directly calls <see cref="File"/> and <see cref="Directory"/> APIs.
/// </summary>
internal sealed class ConfigFileRepository : IConfigFileRepository
{
    private readonly string _configDirectory;
    private readonly ILogger<ConfigFileRepository> _logger;

    public ConfigFileRepository(ConfigUIOptions options, ILogger<ConfigFileRepository> logger)
    {
        _configDirectory = options.ConfigDirectory ?? Directory.GetCurrentDirectory();
        _logger = logger;
    }

    /// <inheritdoc/>
    public IEnumerable<string> DiscoverFiles()
    {
        if (!Directory.Exists(_configDirectory))
        {
            _logger.LogWarning("Config directory does not exist: {Dir}", _configDirectory);
            return Enumerable.Empty<string>();
        }

        return Directory
            .EnumerateFiles(_configDirectory, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f);
    }

    /// <inheritdoc/>
    public async Task<string> ReadAllTextAsync(string fullPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Reading file: {Path}", fullPath);
        return await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task WriteAllTextAsync(string fullPath, string content, CancellationToken ct = default)
    {
        _logger.LogDebug("Writing file: {Path}", fullPath);
        await File.WriteAllTextAsync(fullPath, content, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken ct = default)
    {
        _logger.LogDebug("Copying {Source} -> {Destination}", sourcePath, destinationPath);
        var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(destinationPath, content, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool FileExists(string fullPath) => File.Exists(fullPath);

    /// <inheritdoc/>
    public DateTimeOffset GetLastWriteTime(string fullPath) =>
        File.Exists(fullPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero)
            : DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public string ResolvePath(string fileName) =>
        Path.Combine(_configDirectory, fileName);

    /// <inheritdoc/>
    public IEnumerable<string> DiscoverBackups(string fullPath) =>
        Directory
            .EnumerateFiles(
                Path.GetDirectoryName(fullPath) ?? _configDirectory,
                Path.GetFileName(fullPath) + ".*.bak",
                SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f);
}
