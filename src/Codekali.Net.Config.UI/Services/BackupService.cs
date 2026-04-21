using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Creates and manages timestamped <c>.bak</c> backup copies of appsettings files.
/// </summary>
internal sealed class BackupService(IConfigFileRepository repository,
    ConfigUIOptions options,
    ILogger<BackupService> logger) : IBackupService
{

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CreateBackupAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            var sourcePath = repository.ResolvePath(fileName);

            if (!repository.FileExists(sourcePath))
                return OperationResult<string>.Failure($"Cannot back up '{fileName}': file does not exist.");

            // e.g. appsettings.json.20240101T153045.bak
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
            var backupPath = sourcePath + $".{timestamp}.bak";
            var fullBackupPath = ResolveBackupPath(backupPath);
            await repository.CopyFileAsync(sourcePath, fullBackupPath, ct).ConfigureAwait(false);

            logger.LogInformation("Backup created: {BackupPath}", fullBackupPath);
            return OperationResult<string>.Success(fullBackupPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create backup for {FileName}", fileName);
            return OperationResult<string>.Failure($"Backup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<string>>> ListBackupsAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            var fullPath = repository.ResolvePath(fileName);
            var backups = repository.DiscoverBackups(fullPath).ToList();
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Success(backups));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list backups for {FileName}", fileName);
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Failure(ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> RestoreBackupAsync(string backupPath, CancellationToken ct = default)
    {
        try
        {
            var resolvedBackupPath = ResolveBackupPath(backupPath);
            if (!repository.FileExists(resolvedBackupPath))
                return OperationResult.Failure($"Backup file not found: {resolvedBackupPath}");

            // Derive original file name: strip the ".{timestamp}.bak" suffix
            // Pattern: <originalPath>.<timestamp>.bak  → remove last two extensions
            var withoutBak = Path.ChangeExtension(resolvedBackupPath, null);   // removes .bak
            var originalPath = Path.ChangeExtension(withoutBak, null); // removes .{timestamp}

            if (string.IsNullOrWhiteSpace(originalPath))
                return OperationResult.Failure("Could not determine the original file path from the backup name.");

            // Safety: back up current state before overwriting
            var currentFileName = Path.GetFileName(originalPath);
            var preRestoreBackup = await CreateBackupAsync(currentFileName, ct).ConfigureAwait(false);
            if (!preRestoreBackup.IsSuccess)
            {
                logger.LogWarning("Could not create pre-restore backup: {Error}", preRestoreBackup.Error);
                // Non-fatal — proceed with the restore
            }

            await repository.CopyFileAsync(resolvedBackupPath, originalPath, ct).ConfigureAwait(false);
            logger.LogInformation("Restored {OriginalPath} from {BackupPath}", originalPath, resolvedBackupPath);

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to restore backup {BackupPath}", backupPath);
            return OperationResult.Failure($"Restore failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CreateNamedBackupAsync(
        string fileName, string backupName, CancellationToken ct = default)
    {
        try
        {
            var sourcePath = repository.ResolvePath(fileName);
            if (!repository.FileExists(sourcePath))
                return OperationResult<string>.Failure($"Cannot back up '{fileName}': file does not exist.");

            // Sanitise the name so it is safe as a file-name segment.
            var safeName = string.Concat(backupName
                .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");

            var backupPath = sourcePath + $".{safeName}.bak";
            var fullBackupPath = ResolveBackupPath(backupPath);
            await repository.CopyFileAsync(sourcePath, fullBackupPath, ct).ConfigureAwait(false);
            logger.LogInformation("Named backup created: {BackupPath}", fullBackupPath);
            return OperationResult<string>.Success(fullBackupPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create named backup for {FileName}", fileName);
            return OperationResult<string>.Failure($"Backup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<string> GetNextSuggestedNameAsync(string fileName, CancellationToken ct = default)
    {
        var prefix = options.BackupVersionPrefix;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            // No prefix configured — suggest a human-readable timestamp.
            return Task.FromResult(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        }

        // Find the highest minor version already used for this prefix + file.
        var fullPath = repository.ResolvePath(fileName);
        var backups = repository.DiscoverBackups(fullPath).ToList();

        // Pattern: <fullPath>.<prefix>.<minor>.bak  e.g.  appsettings.json.v1.3.bak
        var pattern = $".{prefix}.";
        int maxMinor = -1;
        foreach (var b in backups)
        {
            var seg = Path.GetFileName(b); // appsettings.json.v1.3.bak
            var idx = seg.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var after = seg[(idx + pattern.Length)..]; // "3.bak"
            var dotBak = after.IndexOf(".bak", StringComparison.OrdinalIgnoreCase);
            if (dotBak < 0) continue;
            if (int.TryParse(after[..dotBak], out var minor) && minor > maxMinor)
                maxMinor = minor;
        }

        return Task.FromResult($"{prefix}.{maxMinor + 1}");
    }

    /// <inheritdoc/>
    public async Task<OperationResult<(string current, string backup)>> GetDiffContentAsync(
        string fileName, string backupPath, CancellationToken ct = default)
    {
        try
        {
            var fullPath = repository.ResolvePath(fileName);
            if (!repository.FileExists(fullPath))
                return OperationResult<(string, string)>.Failure($"File not found: {fileName}");
            var fullBackupPath = ResolveBackupPath(backupPath);
            if (!repository.FileExists(fullBackupPath))
                return OperationResult<(string, string)>.Failure($"Backup not found: {backupPath}");

            var current = await repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            var backup = await repository.ReadAllTextAsync(fullBackupPath, ct).ConfigureAwait(false);
            return OperationResult<(string, string)>.Success((current, backup));
        }
        catch (Exception ex)
        {
            return OperationResult<(string, string)>.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> DeleteBackupAsync(string backupPath, CancellationToken ct = default)
    {
        try
        {
            var fullBackupPath = ResolveBackupPath(backupPath);
            if (!repository.FileExists(fullBackupPath))
                return OperationResult.Failure($"Backup not found: {backupPath}");
            await Task.Run(() => File.Delete(fullBackupPath), ct).ConfigureAwait(false);
            logger.LogInformation("Deleted backup: {BackupPath}", backupPath);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete backup {BackupPath}", backupPath);
            return OperationResult.Failure(ex.Message);
        }
    }

    private string ResolveBackupPath(string backupPath) =>
        Path.IsPathRooted(backupPath) ? backupPath : repository.ResolvePath(backupPath);
}
