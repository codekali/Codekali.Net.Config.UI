using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Creates and manages backup copies of appsettings files before any write operation.
/// A backup is a timestamped <c>.bak</c> file written to the same directory as the original.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a timestamped backup of the specified file.
    /// The backup is written alongside the original, e.g.
    /// <c>appsettings.json.20240101T120000.bak</c>.
    /// </summary>
    /// <param name="fileName">The appsettings file name to back up.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The full path of the created backup file, or a failure result.</returns>
    Task<OperationResult<string>> CreateBackupAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns all backup files for the named appsettings file, ordered newest-first.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> ListBackupsAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Restores the named appsettings file from the specified backup path.
    /// Creates a backup of the current file before restoring.
    /// </summary>
    Task<OperationResult> RestoreBackupAsync(string backupPath, CancellationToken ct = default);

    /// <summary>
    /// Creates a backup with an explicit <paramref name="backupName"/> label
    /// (e.g. "v1.2" or "before-payment-refactor").
    /// The file is named <c>{fileName}.{backupName}.bak</c>.
    /// </summary>
    Task<OperationResult<string>> CreateNamedBackupAsync(
        string fileName, string backupName, CancellationToken ct = default);

    /// <summary>
    /// Returns the next suggested backup name given the configured
    /// <see cref="ConfigUIOptions.BackupVersionPrefix"/> and the existing backups.
    /// E.g. if prefix is "v1" and "v1.3.bak" exists, returns "v1.4".
    /// Falls back to a timestamp string when no prefix is configured.
    /// </summary>
    Task<string> GetNextSuggestedNameAsync(string fileName, CancellationToken ct = default);

    /// <summary>
    /// Returns the raw JSON of both the current file and the specified backup
    /// for a side-by-side diff preview.
    /// </summary>
    Task<OperationResult<(string current, string backup)>> GetDiffContentAsync(
        string fileName, string backupPath, CancellationToken ct = default);

    /// <summary>Deletes the backup at <paramref name="backupPath"/>.</summary>
    Task<OperationResult> DeleteBackupAsync(string backupPath, CancellationToken ct = default);
}
