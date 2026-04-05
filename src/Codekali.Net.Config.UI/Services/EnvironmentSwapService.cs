using System.Text.Json;
using System.Text.Json.Nodes;
using Codekali.Net.Config.UI.Extensions;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

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
/// </remarks>
internal sealed class EnvironmentSwapService : IEnvironmentSwapService
{
    private readonly IConfigFileRepository _repository;
    private readonly IBackupService _backupService;
    private readonly ILogger<EnvironmentSwapService> _logger;

    public EnvironmentSwapService(
        IConfigFileRepository repository,
        IBackupService backupService,
        ILogger<EnvironmentSwapService> logger)
    {
        _repository = repository;
        _backupService = backupService;
        _logger = logger;
    }

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

        // ── Back up both files before any modification ─────────────────────
        await _backupService.CreateBackupAsync(request.SourceFile, ct).ConfigureAwait(false);
        await _backupService.CreateBackupAsync(request.TargetFile, ct).ConfigureAwait(false);

        // ── Load raw text for the comment-preserving write path ────────────
        var sourceRaw = await ReadRawAsync(request.SourceFile, ct).ConfigureAwait(false);
        var targetRaw = await ReadRawOrEmptyAsync(request.TargetFile, ct).ConfigureAwait(false);

        // ── Apply the operation key by key via string surgery ──────────────
        //
        // Strategy:
        //   1. Extract the value JSON for each key from the System.Text.Json tree
        //      (already parsed above for the collision check — no extra I/O).
        //   2. Use JsonCommentPreservingWriter.SetValue to splice it into the
        //      target raw text — comments in the target file are preserved.
        //   3. For Move: use RemoveKey to excise the key from the source raw
        //      text — comments in the source file are preserved.
        //
        // We mutate the raw strings in a loop rather than writing to disk per
        // key, then do a single write per file at the end.

        foreach (var key in request.Keys)
        {
            var sourceNode = JsonHelper.GetNode(sourceRoot, key);
            if (sourceNode is null)
            {
                _logger.LogWarning(
                    "Key '{Key}' not found in source '{Source}' — skipping.",
                    key, request.SourceFile);
                continue;
            }

            // Serialise the value from the System.Text.Json node into a JSON
            // string fragment that SetValue can parse back in.
            var valueJson = sourceNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

            try
            {
                targetRaw = JsonCommentPreservingWriter.SetValue(targetRaw, key, valueJson);

                if (request.Operation == SwapOperation.Move)
                    sourceRaw = JsonCommentPreservingWriter.RemoveKey(sourceRaw, key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply swap for key '{Key}' — operation aborted.", key);
                return OperationResult.Failure(
                    $"Failed to process key '{key}': {ex.Message}");
            }
        }

        // ── Persist ────────────────────────────────────────────────────────
        var targetPath = _repository.ResolvePath(request.TargetFile);
        await _repository.WriteAllTextAsync(targetPath, targetRaw, ct).ConfigureAwait(false);

        if (request.Operation == SwapOperation.Move)
        {
            var sourcePath = _repository.ResolvePath(request.SourceFile);
            await _repository.WriteAllTextAsync(sourcePath, sourceRaw, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
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

        var sourceFlat = JsonHelper.Flatten(sourceRoot);
        var targetFlat = JsonHelper.Flatten(targetRoot);
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

    private async Task<(JsonObject? root, string? error)> LoadRootForReadAsync(
        string fileName, CancellationToken ct)
    {
        var fullPath = _repository.ResolvePath(fileName);
        if (!_repository.FileExists(fullPath))
            return (null, $"File not found: {fileName}");

        try
        {
            var raw = await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
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
        var fullPath = _repository.ResolvePath(fileName);
        return await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
    }

    private async Task<string> ReadRawOrEmptyAsync(string fileName, CancellationToken ct)
    {
        var fullPath = _repository.ResolvePath(fileName);
        return _repository.FileExists(fullPath)
            ? await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false)
            : "{}";
    }
}