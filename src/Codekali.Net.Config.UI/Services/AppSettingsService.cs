using Codekali.Net.Config.UI.Extensions;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Implements CRUD operations over appsettings JSON files.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read path</b> — uses <see cref="JsonHelper"/> (<c>System.Text.Json</c>).
/// Fast, allocation-efficient, no third-party dependency.
/// </para>
/// <para>
/// <b>Write path</b> — uses <see cref="JsonCommentPreservingWriter"/> (Newtonsoft.Json).
/// Loads the raw file text with <c>CommentHandling.Load</c> before applying any
/// mutation, so <c>//</c> and <c>/* */</c> developer comments are preserved
/// verbatim across every Add, Update, Delete, and Save Raw operation.
/// </para>
/// <para>
/// <b>Backup policy</b> — backups are NOT created automatically on every write.
/// Users trigger backups explicitly via the 💾 Backup button in the UI.
/// </para>
/// </remarks>
internal sealed class AppSettingsService : IAppSettingsService
{
    private readonly IConfigFileRepository _repository;
    private readonly IBackupService _backupService;
    private readonly ConfigUIOptions _options;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(
        IConfigFileRepository repository,
        IBackupService backupService,
        ConfigUIOptions options,
        ILogger<AppSettingsService> logger)
    {
        _repository = repository;
        _backupService = backupService;
        _options = options;
        _logger = logger;
    }

    // ── Read operations (System.Text.Json) ───────────────────────────────

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<AppSettingsFile>>> GetAllFilesAsync(
        CancellationToken ct = default)
    {
        try
        {
            var files = _repository
                .DiscoverFiles()
                .Select(fullPath =>
                {
                    var fileName = Path.GetFileName(fullPath);
                    return new AppSettingsFile
                    {
                        FileName = fileName,
                        FullPath = fullPath,
                        Environment = ExtractEnvironment(fileName),
                        Exists = _repository.FileExists(fullPath),
                        LastModified = _repository.GetLastWriteTime(fullPath)
                    };
                })
                .ToList();

            return Task.FromResult(
                OperationResult<IReadOnlyList<AppSettingsFile>>.Success(files));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover appsettings files");
            return Task.FromResult(
                OperationResult<IReadOnlyList<AppSettingsFile>>.Failure(ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IReadOnlyList<ConfigEntry>>> GetEntriesAsync(
        string fileName, CancellationToken ct = default)
    {
        var rawResult = await GetRawJsonAsync(fileName, ct).ConfigureAwait(false);
        if (!rawResult.IsSuccess)
            return OperationResult<IReadOnlyList<ConfigEntry>>.Failure(rawResult.Error!);

        // Read path: System.Text.Json — fast, no Newtonsoft dependency for reads.
        var root = JsonHelper.ParseObject(rawResult.Value!);
        if (root is null)
            return OperationResult<IReadOnlyList<ConfigEntry>>.Failure(
                $"'{fileName}' does not contain a valid JSON object.");

        var entries = JsonHelper.ToEntryTree(root, fileName, _options.MaskSensitiveValues);
        return OperationResult<IReadOnlyList<ConfigEntry>>.Success(entries);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetRawJsonAsync(
        string fileName, CancellationToken ct = default)
    {
        try
        {
            var fullPath = _repository.ResolvePath(fileName);
            if (!_repository.FileExists(fullPath))
                return OperationResult<string>.Failure($"File not found: {fileName}");

            var raw = await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            return OperationResult<string>.Success(raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FileName}", fileName);
            return OperationResult<string>.Failure(ex.Message);
        }
    }

    // ── Write operations (Newtonsoft.Json — preserves comments) ──────────

    /// <inheritdoc/>
    /// <remarks>
    /// Changes are written to disk immediately. For restart-free reload the
    /// host application must consume configuration via
    /// <c>IOptionsSnapshot&lt;T&gt;</c> or <c>IOptionsMonitor&lt;T&gt;</c>
    /// rather than <c>IOptions&lt;T&gt;</c>.
    /// </remarks>
    public async Task<OperationResult> AddEntryAsync(
        string fileName, string keyPath, string jsonValue, CancellationToken ct = default)
    {
        try
        {
            // Conflict check uses the fast System.Text.Json read path.
            var (root, loadError) = await LoadRootForReadAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            if (JsonHelper.GetNode(root, keyPath) is not null)
                return OperationResult.Failure(
                    $"Key '{keyPath}' already exists in '{fileName}'. Use Update to change its value.");

            return await PersistMutationAsync(
                fileName,
                raw => JsonCommentPreservingWriter.SetValue(raw, keyPath, jsonValue),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Changes are written to disk immediately. For restart-free reload the
    /// host application must consume configuration via
    /// <c>IOptionsSnapshot&lt;T&gt;</c> or <c>IOptionsMonitor&lt;T&gt;</c>
    /// rather than <c>IOptions&lt;T&gt;</c>.
    /// </remarks>
    public async Task<OperationResult> UpdateEntryAsync(
        string fileName, string keyPath, string jsonValue, CancellationToken ct = default)
    {
        try
        {
            // Existence check uses the fast System.Text.Json read path.
            var (root, loadError) = await LoadRootForReadAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            if (JsonHelper.GetNode(root, keyPath) is null)
                return OperationResult.Failure(
                    $"Key '{keyPath}' does not exist in '{fileName}'. Use Add to create it.");

            return await PersistMutationAsync(
                fileName,
                raw => JsonCommentPreservingWriter.SetValue(raw, keyPath, jsonValue),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Changes are written to disk immediately. For restart-free reload the
    /// host application must consume configuration via
    /// <c>IOptionsSnapshot&lt;T&gt;</c> or <c>IOptionsMonitor&lt;T&gt;</c>
    /// rather than <c>IOptions&lt;T&gt;</c>.
    /// </remarks>
    public async Task<OperationResult> DeleteEntryAsync(
        string fileName, string keyPath, CancellationToken ct = default)
    {
        try
        {
            // Existence check uses the fast System.Text.Json read path.
            var (root, loadError) = await LoadRootForReadAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            if (JsonHelper.GetNode(root, keyPath) is null)
                return OperationResult.Failure(
                    $"Key '{keyPath}' does not exist in '{fileName}'.");

            return await PersistMutationAsync(
                fileName,
                raw => JsonCommentPreservingWriter.RemoveKey(raw, keyPath),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The raw content is validated for well-formed JSON (comments permitted)
    /// and written as-is — no re-serialisation occurs, so all formatting and
    /// comments authored by the developer are preserved exactly.
    /// Changes are written to disk immediately. For restart-free reload the
    /// host application must consume configuration via
    /// <c>IOptionsSnapshot&lt;T&gt;</c> or <c>IOptionsMonitor&lt;T&gt;</c>
    /// rather than <c>IOptions&lt;T&gt;</c>.
    /// </remarks>
    public async Task<OperationResult> SaveRawJsonAsync(
        string fileName, string rawJson, CancellationToken ct = default)
    {
        // Validate with Newtonsoft so comments are accepted as valid syntax.
        var validationError = JsonCommentPreservingWriter.Validate(rawJson);
        if (validationError is not null)
            return OperationResult.Failure($"Invalid JSON: {validationError}");

        try
        {
            var fullPath = _repository.ResolvePath(fileName);

            // Write the raw content verbatim — do NOT re-serialise.
            // The user typed this; keep it exactly as authored.
            await _repository.WriteAllTextAsync(fullPath, rawJson, ct).ConfigureAwait(false);
            _logger.LogInformation("Saved raw JSON to {FileName}", fileName);
            return OperationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveRawJson failed for {FileName}", fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Reads the file and applies a comment-preserving mutation via
    /// <see cref="JsonCommentPreservingWriter"/>, then persists the result.
    /// </summary>
    /// <param name="fileName">The appsettings file name.</param>
    /// <param name="mutate">
    /// <param name="ct"></param>
    /// A function that receives the current raw JSON text and returns the
    /// mutated raw JSON text (with comments intact).
    /// </param>
    private async Task<OperationResult> PersistMutationAsync(
        string fileName,
        Func<string, string> mutate,
        CancellationToken ct)
    {
        var fullPath = _repository.ResolvePath(fileName);

        if (!_repository.FileExists(fullPath))
            return OperationResult.Failure($"File not found: {fileName}");

        // Read the current raw text — comments included.
        var raw = await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);

        // Apply the mutation (Set or Remove) via Newtonsoft — comments survive.
        string updated;
        try
        {
            updated = mutate(raw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON mutation failed for {FileName}", fileName);
            return OperationResult.Failure($"Failed to apply change: {ex.Message}");
        }

        await _repository.WriteAllTextAsync(fullPath, updated, ct).ConfigureAwait(false);
        _logger.LogInformation("Persisted changes to {FileName}", fileName);
        return OperationResult.Success();
    }

    /// <summary>
    /// Loads the file into a <c>System.Text.Json</c> <see cref="JsonObject"/>
    /// for read-only operations (existence checks, conflict checks).
    /// Comments are not required here — the tree is discarded after the check.
    /// </summary>
    private async Task<(JsonObject? root, string? error)> LoadRootForReadAsync(
        string fileName, CancellationToken ct)
    {
        var fullPath = _repository.ResolvePath(fileName);
        if (!_repository.FileExists(fullPath))
            return (null, $"File not found: {fileName}");

        var raw = await _repository.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
        var root = JsonHelper.ParseObject(raw);
        if (root is null)
            return (null, $"'{fileName}' does not contain a valid JSON object.");

        return (root, null);
    }

    private static string ExtractEnvironment(string fileName)
    {
        const string prefix = "appsettings.";
        const string suffix = ".json";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return "Unknown";

        var middle = fileName[prefix.Length..];
        if (middle.Equals(suffix.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            return "Base";

        return middle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? middle[..^suffix.Length]
            : middle;
    }
}