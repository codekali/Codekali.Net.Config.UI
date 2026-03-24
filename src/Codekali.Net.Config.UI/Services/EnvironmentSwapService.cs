using System.Text.Json.Nodes;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Implements Move, Copy, and Compare operations across appsettings environment files.
/// All write operations are preceded by backups of both affected files.
/// </summary>
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
    public async Task<OperationResult> ExecuteSwapAsync(SwapRequest request, CancellationToken ct = default)
    {
        if (request.Keys.Count == 0)
            return OperationResult.Failure("No keys specified in the swap request.");

        if (string.Equals(request.SourceFile, request.TargetFile, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Failure("Source and target files must be different.");

        // Load both files
        var (sourceRoot, sourceErr) = await LoadRootAsync(request.SourceFile, ct).ConfigureAwait(false);
        if (sourceRoot is null) return OperationResult.Failure(sourceErr!);

        var (targetRoot, _) = await LoadRootAsync(request.TargetFile, ct).ConfigureAwait(false);
        // Target is allowed to be empty / non-existent — we will create it
        targetRoot ??= new JsonObject();

        // Collision check
        if (!request.OverwriteExisting)
        {
            var conflicts = request.Keys
                .Where(key => JsonHelper.GetNode(targetRoot, key) is not null)
                .ToList();

            if (conflicts.Count > 0)
                return OperationResult.Failure(
                    $"The following keys already exist in '{request.TargetFile}' and OverwriteExisting is false: " +
                    string.Join(", ", conflicts));
        }

        // Back up both files before any modification
        await _backupService.CreateBackupAsync(request.SourceFile, ct).ConfigureAwait(false);
        await _backupService.CreateBackupAsync(request.TargetFile, ct).ConfigureAwait(false);

        // Perform the operation
        foreach (var key in request.Keys)
        {
            var node = JsonHelper.GetNode(sourceRoot, key);
            if (node is null)
            {
                _logger.LogWarning("Key '{Key}' not found in source '{Source}' — skipping.", key, request.SourceFile);
                continue;
            }

            // Deep clone the node so it can be inserted into a different JsonObject tree
            var cloned = node.DeepClone();
            JsonHelper.SetNode(targetRoot, key, cloned);

            if (request.Operation == SwapOperation.Move)
                JsonHelper.RemoveNode(sourceRoot, key);
        }

        // Persist changes
        var targetPath = _repository.ResolvePath(request.TargetFile);
        await _repository.WriteAllTextAsync(targetPath, JsonHelper.Serialize(targetRoot), ct)
            .ConfigureAwait(false);

        if (request.Operation == SwapOperation.Move)
        {
            var sourcePath = _repository.ResolvePath(request.SourceFile);
            await _repository.WriteAllTextAsync(sourcePath, JsonHelper.Serialize(sourceRoot), ct)
                .ConfigureAwait(false);
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
        var (sourceRoot, sourceErr) = await LoadRootAsync(sourceFile, ct).ConfigureAwait(false);
        if (sourceRoot is null) return OperationResult<DiffResult>.Failure(sourceErr!);

        var (targetRoot, targetErr) = await LoadRootAsync(targetFile, ct).ConfigureAwait(false);
        if (targetRoot is null) return OperationResult<DiffResult>.Failure(targetErr!);

        var sourceFlat = JsonHelper.Flatten(sourceRoot);
        var targetFlat = JsonHelper.Flatten(targetRoot);

        var allKeys = sourceFlat.Keys.Union(targetFlat.Keys, StringComparer.OrdinalIgnoreCase).ToList();

        var diff = new DiffResult
        {
            SourceFile = sourceFile,
            TargetFile = targetFile
        };

        foreach (var key in allKeys)
        {
            var inSource = sourceFlat.TryGetValue(key, out var sv);
            var inTarget = targetFlat.TryGetValue(key, out var tv);

            if (inSource && !inTarget)
            {
                diff.OnlyInSource.Add(key);
            }
            else if (!inSource && inTarget)
            {
                diff.OnlyInTarget.Add(key);
            }
            else if (string.Equals(sv, tv, StringComparison.Ordinal))
            {
                diff.Identical.Add(key);
            }
            else
            {
                diff.ValueDifferences.Add(new DiffEntry
                {
                    Key = key,
                    SourceValue = sv,
                    TargetValue = tv
                });
            }
        }

        return OperationResult<DiffResult>.Success(diff);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IReadOnlyList<string>>> FindConflictsAsync(
        string targetFile, IEnumerable<string> keys, CancellationToken ct = default)
    {
        var (targetRoot, targetErr) = await LoadRootAsync(targetFile, ct).ConfigureAwait(false);
        if (targetRoot is null) return OperationResult<IReadOnlyList<string>>.Failure(targetErr!);

        var conflicts = keys
            .Where(key => JsonHelper.GetNode(targetRoot, key) is not null)
            .ToList();

        return OperationResult<IReadOnlyList<string>>.Success(conflicts);
    }

    // ── private helpers ──────────────────────────────────────────────────────

    private async Task<(JsonObject? root, string? error)> LoadRootAsync(
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
}
