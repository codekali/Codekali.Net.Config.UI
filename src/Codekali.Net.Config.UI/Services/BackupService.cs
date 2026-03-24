using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Creates and manages timestamped <c>.bak</c> backup copies of appsettings files.
/// </summary>
internal sealed class BackupService : IBackupService
{
    private readonly IConfigFileRepository _repository;
    private readonly ILogger<BackupService> _logger;

    public BackupService(IConfigFileRepository repository, ILogger<BackupService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> CreateBackupAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            var sourcePath = _repository.ResolvePath(fileName);

            if (!_repository.FileExists(sourcePath))
                return OperationResult<string>.Failure($"Cannot back up '{fileName}': file does not exist.");

            // e.g. appsettings.json.20240101T153045.bak
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
            var backupPath = sourcePath + $".{timestamp}.bak";

            await _repository.CopyFileAsync(sourcePath, backupPath, ct).ConfigureAwait(false);

            _logger.LogInformation("Backup created: {BackupPath}", backupPath);
            return OperationResult<string>.Success(backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup for {FileName}", fileName);
            return OperationResult<string>.Failure($"Backup failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<string>>> ListBackupsAsync(string fileName, CancellationToken ct = default)
    {
        try
        {
            var fullPath = _repository.ResolvePath(fileName);
            var backups = _repository.DiscoverBackups(fullPath).ToList();
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Success(backups));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list backups for {FileName}", fileName);
            return Task.FromResult(OperationResult<IReadOnlyList<string>>.Failure(ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> RestoreBackupAsync(string backupPath, CancellationToken ct = default)
    {
        try
        {
            if (!_repository.FileExists(backupPath))
                return OperationResult.Failure($"Backup file not found: {backupPath}");

            // Derive original file name: strip the ".{timestamp}.bak" suffix
            // Pattern: <originalPath>.<timestamp>.bak  → remove last two extensions
            var withoutBak = Path.ChangeExtension(backupPath, null);   // removes .bak
            var originalPath = Path.ChangeExtension(withoutBak, null); // removes .{timestamp}

            if (string.IsNullOrWhiteSpace(originalPath))
                return OperationResult.Failure("Could not determine the original file path from the backup name.");

            // Safety: back up current state before overwriting
            var currentFileName = Path.GetFileName(originalPath);
            var preRestoreBackup = await CreateBackupAsync(currentFileName, ct).ConfigureAwait(false);
            if (!preRestoreBackup.IsSuccess)
            {
                _logger.LogWarning("Could not create pre-restore backup: {Error}", preRestoreBackup.Error);
                // Non-fatal — proceed with the restore
            }

            await _repository.CopyFileAsync(backupPath, originalPath, ct).ConfigureAwait(false);
            _logger.LogInformation("Restored {OriginalPath} from {BackupPath}", originalPath, backupPath);

            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore backup {BackupPath}", backupPath);
            return OperationResult.Failure($"Restore failed: {ex.Message}");
        }
    }
}
