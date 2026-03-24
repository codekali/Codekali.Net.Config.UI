using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Provides CRUD operations over appsettings JSON files.
/// All write operations automatically trigger a backup via <see cref="IBackupService"/> before modifying the file.
/// </summary>
public interface IAppSettingsService
{
    /// <summary>
    /// Discovers all <c>appsettings*.json</c> files in the configured directory.
    /// </summary>
    Task<OperationResult<IReadOnlyList<AppSettingsFile>>> GetAllFilesAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads and parses the full contents of the named file into a tree of <see cref="ConfigEntry"/> objects.
    /// </summary>
    /// <param name="fileName">The file name, e.g. "appsettings.Development.json".</param>
    Task<OperationResult<IReadOnlyList<ConfigEntry>>> GetEntriesAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw JSON string for the named file.
    /// </summary>
    Task<OperationResult<string>> GetRawJsonAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Adds a new key at the specified dot-notation path.
    /// If the key already exists, returns a failure result.
    /// </summary>
    Task<OperationResult> AddEntryAsync(string fileName, string keyPath, string jsonValue, CancellationToken ct = default);

    /// <summary>
    /// Updates the value of an existing key.
    /// Backs up the file before writing.
    /// </summary>
    Task<OperationResult> UpdateEntryAsync(string fileName, string keyPath, string jsonValue, CancellationToken ct = default);

    /// <summary>
    /// Removes the key at the specified dot-notation path.
    /// Backs up the file before writing.
    /// </summary>
    Task<OperationResult> DeleteEntryAsync(string fileName, string keyPath, CancellationToken ct = default);

    /// <summary>
    /// Overwrites the entire file with the supplied raw JSON.
    /// Performs JSON validation before writing and backs up the file first.
    /// </summary>
    Task<OperationResult> SaveRawJsonAsync(string fileName, string rawJson, CancellationToken ct = default);
}
