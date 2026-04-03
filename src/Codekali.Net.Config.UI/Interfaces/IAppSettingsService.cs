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
    /// <param name="ct">A cancellation token.</param>
    Task<OperationResult<IReadOnlyList<ConfigEntry>>> GetEntriesAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw JSON text of <paramref name="fileName"/>.
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
    /// Appends <paramref name="jsonValue"/> as a new item at the end of the
    /// array at <paramref name="keyPath"/>. Creates the array if it does not
    /// exist. Fails if the key exists but is not an array.
    /// </summary>
    Task<OperationResult> AppendArrayItemAsync(string fileName, string keyPath, string jsonValue, CancellationToken ct = default);

    /// <summary>
    /// Removes the array item at zero-based <paramref name="index"/> from the
    /// array at <paramref name="keyPath"/>. Remaining items are re-indexed.
    /// </summary>
    Task<OperationResult> RemoveArrayItemAsync(string fileName, string keyPath, int index, CancellationToken ct = default);

    /// <summary>
    /// Replaces the raw JSON text of <paramref name="fileName"/>.
    /// Validates for well-formed JSON (comments accepted) before writing.
    /// </summary>
    Task<OperationResult> SaveRawJsonAsync(string fileName, string rawJson, CancellationToken ct = default);
}
