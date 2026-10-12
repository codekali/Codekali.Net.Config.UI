using Codekali.Net.Config.UI.Extensions;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Implements Move, Copy, and Compare operations across appsettings environment files.
/// </summary>
/// <remarks>
/// <b>Comment preservation</b><br/>
/// The write path (Move and Copy) uses <see cref="JsonCommentPreservingWriter"/>
/// so that <c>//</c> and <c>/* */</c> comments in both the source and target
/// files survive the operation. The read path (Compare, collision checks) uses
/// <see cref="JsonHelper"/> (<c>System.Text.Json</c>).
/// <br/><br/>
/// <b>Backup throttling</b><br/>
/// When <see cref="SwapRequest.SkipBackup"/> is false (the default), backups are
/// created before every write.  When the Compare row-action buttons fire in quick
/// succession, callers set <see cref="SwapRequest.SkipBackup"/> to true once a
/// backup was created within <see cref="BackupThrottleSeconds"/> seconds.
/// </remarks>
internal sealed class EnvironmentSwapService(
    IConfigFileRepository repository,
    IBackupService backupService,
    ConfigUIOptions options,
    ILogger<EnvironmentSwapService> logger) : IEnvironmentSwapService
{
    /// <summary>
    /// How long (in seconds) a recent backup suppresses a new one when
    /// <see cref="SwapRequest.SkipBackup"/> is true.
    /// </summary>
    public const int BackupThrottleSeconds = 30;

    // Tracks the last backup time per file path so the JS can choose SkipBackup=true
    // when a backup was created within BackupThrottleSeconds.
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastBackupTime = new();

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteSwapAsync(
        SwapRequest request, CancellationToken ct = default)
    {
        if (request.Keys.Count == 0)
            return OperationResult.Failure("No keys specified in the swap request.");

        if (string.Equals(request.SourceFile, request.TargetFile, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Failure("Source and target files must be different.");

        // ── Read both files for collision check (System.Text.Json — fast) ──
        var (sourceRoot, sourceErr) = await LoadRootForReadAsync(request.SourceFile, ct)
            .ConfigureAwait(false);
        if (sourceRoot is null) return OperationResult.Failure(sourceErr!);

        var (targetRoot, _) = await LoadRootForReadAsync(request.TargetFile, ct)
            .ConfigureAwait(false);
        targetRoot ??= new JsonObject();

        // ── Collision check ────────────────────────────────────────────────
        if (!request.OverwriteExisting)
        {
            var conflicts = request.Keys
                .Where(key => JsonHelper.GetNode(targetRoot, key) is not null)
                .ToList();

            if (conflicts.Count > 0)
                return OperationResult.Failure(
                    $"The following keys already exist in '{request.TargetFile}' and " +
                    $"OverwriteExisting is false: {string.Join(", ", conflicts)}");
        }

        // ── Back up both files before any modification (unless throttled) ──
        if (!request.SkipBackup)
        {
            await BackupIfNotThrottledAsync(request.SourceFile, ct).ConfigureAwait(false);
            await BackupIfNotThrottledAsync(request.TargetFile, ct).ConfigureAwait(false);
        }
        else
        {
            logger.LogDebug(
                "SkipBackup=true on swap {Op} — caller is responsible for recent backup.",
                request.Operation);
        }

        // ── Load raw text for the comment-preserving write path ────────────
        var sourceRaw = await ReadRawAsync(request.SourceFile, ct).ConfigureAwait(false);
        var targetRaw = await ReadRawOrEmptyAsync(request.TargetFile, ct).ConfigureAwait(false);

        // ── Apply the operation key by key via string surgery ──────────────
        foreach (var key in request.Keys)
        {
            var sourceNode = JsonHelper.GetNode(sourceRoot, key);
            if (sourceNode is null)
            {
                logger.LogWarning(
                    "Key '{Key}' not found in source '{Source}' — skipping.",
                    key, request.SourceFile);
                continue;
            }

            var valueJson = sourceNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            try
            {
                targetRaw = JsonCommentPreservingWriter.SetValue(targetRaw, key, valueJson);

                if (request.Operation == SwapOperation.Move)
                    sourceRaw = JsonCommentPreservingWriter.RemoveKey(sourceRaw, key);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to apply swap for key '{Key}' — operation aborted.", key);
                return OperationResult.Failure(
                    $"Failed to process key '{key}': {ex.Message}");
            }
        }

        // ── Persist ────────────────────────────────────────────────────────
        var targetPath = repository.ResolvePath(request.TargetFile);
        await repository.WriteAllTextAsync(targetPath, targetRaw, ct).ConfigureAwait(false);

        if (request.Operation == SwapOperation.Move)
        {
            var sourcePath = repository.ResolvePath(request.SourceFile);
            await repository.WriteAllTextAsync(sourcePath, sourceRaw, ct).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Swap complete: {Op} {Count} key(s) from '{Source}' to '{Target}'",
            request.Operation, request.Keys.Count, request.SourceFile, request.TargetFile);

        return OperationResult.Success();
    }

    /// <inheritdoc/>
    public async Task<OperationResult<DiffResult>> CompareFilesAsync(
        string sourceFile, string targetFile, CancellationToken ct = default)
    {
        var (sourceRoot, sourceErr) = await LoadRootForReadAsync(sourceFile, ct)
            .ConfigureAwait(false);
        if (sourceRoot is null)
            return OperationResult<DiffResult>.Failure(sourceErr!);

        var (targetRoot, targetErr) = await LoadRootForReadAsync(targetFile, ct)
            .ConfigureAwait(false);
        if (targetRoot is null)
            return OperationResult<DiffResult>.Failure(targetErr!);

        // Flatten with sensitive value masking so secret values never appear in diff rows.
        // The UI can call /value to reveal a specific key on demand.
        var maskSensitive = options.MaskSensitiveValues;
        var sourceFlat = JsonHelper.Flatten(sourceRoot, maskSensitive);
        var targetFlat = JsonHelper.Flatten(targetRoot, maskSensitive);

        var allKeys = sourceFlat.Keys
            .Union(targetFlat.Keys, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var diff = new DiffResult { SourceFile = sourceFile, TargetFile = targetFile };

        foreach (var key in allKeys)
        {
            var inSource = sourceFlat.TryGetValue(key, out var sv);
            var inTarget = targetFlat.TryGetValue(key, out var tv);

            if (inSource && !inTarget) diff.OnlyInSource.Add(key);
            else if (!inSource && inTarget) diff.OnlyInTarget.Add(key);
            else if (string.Equals(sv, tv, StringComparison.Ordinal)) diff.Identical.Add(key);
            else diff.ValueDifferences.Add(new DiffEntry { Key = key, SourceValue = sv, TargetValue = tv });
        }

        return OperationResult<DiffResult>.Success(diff);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IReadOnlyList<string>>> FindConflictsAsync(
        string targetFile, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var (targetRoot, targetErr) = await LoadRootForReadAsync(targetFile, ct)
            .ConfigureAwait(false);
        if (targetRoot is null)
            return OperationResult<IReadOnlyList<string>>.Failure(targetErr!);

        var conflicts = keys
            .Where(key => JsonHelper.GetNode(targetRoot, key) is not null)
            .ToList();

        return OperationResult<IReadOnlyList<string>>.Success(conflicts);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private async Task BackupIfNotThrottledAsync(string fileName, CancellationToken ct)
    {
        var fullPath = repository.ResolvePath(fileName);
        var now = DateTimeOffset.UtcNow;

        if (_lastBackupTime.TryGetValue(fullPath, out var last) &&
            (now - last).TotalSeconds < BackupThrottleSeconds)
        {
            logger.LogDebug(
                "Backup throttled for '{File}' — last backup was {Ago:F0}s ago.",
                fileName, (now - last).TotalSeconds);
            return;
        }

        var result = await backupService.CreateBackupAsync(fileName, ct).ConfigureAwait(false);
        if (result.IsSuccess)
            _lastBackupTime[fullPath] = now;
    }

    private async Task<(JsonObject? root, string? error)> LoadRootForReadAsync(
        string fileName, CancellationToken ct)
    {
        var fullPath = repository.ResolvePath(fileName);
        if (!repository.FileExists(fullPath))
            return (null, $"File not found: {fileName}");

        try
        {
            var raw = await repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            var root = JsonHelper.ParseObject(raw);
            if (root is null)
                return (null, $"'{fileName}' does not contain a valid JSON object.");
            return (root, null);
        }
        catch (Exception ex)
        {
            return (null, $"Failed to read '{fileName}': {ex.Message}");
        }
    }

    private async Task<string> ReadRawAsync(string fileName, CancellationToken ct)
    {
        var fullPath = repository.ResolvePath(fileName);
        return await repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
    }

    private async Task<string> ReadRawOrEmptyAsync(string fileName, CancellationToken ct)
    {
        var fullPath = repository.ResolvePath(fileName);
        return repository.FileExists(fullPath)
            ? await repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false)
            : "{}";
    }
}