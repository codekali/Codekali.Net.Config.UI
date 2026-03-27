using System.Text.Json.Nodes;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Implements CRUD operations over appsettings JSON files.
/// All write operations create a backup before modifying the file.
/// </summary>
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

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<AppSettingsFile>>> GetAllFilesAsync(CancellationToken ct = default)
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

    /// <inheritdoc/>
    public async Task<OperationResult> AddEntryAsync(
        string fileName, string keyPath, string jsonValue, CancellationToken ct = default)
    {
        try
        {
            var (root, loadError) = await LoadRootAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            // Prevent overwriting an existing key
            if (JsonHelper.GetNode(root, keyPath) is not null)
                return OperationResult.Failure(
                    $"Key '{keyPath}' already exists in '{fileName}'. Use Update to change its value.");

            var newNode = ParseValueNode(jsonValue);
            if (newNode is null)
                return OperationResult.Failure($"Invalid JSON value: {jsonValue}");

            JsonHelper.SetNode(root, keyPath, newNode);
            return await PersistAsync(fileName, root, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AddEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> UpdateEntryAsync(
        string fileName, string keyPath, string jsonValue, CancellationToken ct = default)
    {
        try
        {
            var (root, loadError) = await LoadRootAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            if (JsonHelper.GetNode(root, keyPath) is null)
                return OperationResult.Failure(
                    $"Key '{keyPath}' does not exist in '{fileName}'. Use Add to create it.");

            var newNode = ParseValueNode(jsonValue);
            if (newNode is null)
                return OperationResult.Failure($"Invalid JSON value: {jsonValue}");

            JsonHelper.SetNode(root, keyPath, newNode);
            return await PersistAsync(fileName, root, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> DeleteEntryAsync(
        string fileName, string keyPath, CancellationToken ct = default)
    {
        try
        {
            var (root, loadError) = await LoadRootAsync(fileName, ct).ConfigureAwait(false);
            if (root is null) return OperationResult.Failure(loadError!);

            if (!JsonHelper.RemoveNode(root, keyPath))
                return OperationResult.Failure(
                    $"Key '{keyPath}' does not exist in '{fileName}'.");

            return await PersistAsync(fileName, root, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeleteEntry failed for {Key} in {File}", keyPath, fileName);
            return OperationResult.Failure(ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult> SaveRawJsonAsync(
        string fileName, string rawJson, CancellationToken ct = default)
    {
        var validationError = JsonHelper.Validate(rawJson);
        if (validationError is not null)
            return OperationResult.Failure($"Invalid JSON: {validationError}");

        try
        {
            var fullPath = _repository.ResolvePath(fileName);
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

    // ── private helpers ──────────────────────────────────────────────────────

    private async Task<(JsonObject? root, string? error)> LoadRootAsync(
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

    private async Task<OperationResult> PersistAsync(
        string fileName, JsonObject root, CancellationToken ct)
    {
        // Backup is NOT triggered here automatically.
        // Users trigger backups explicitly via the "Create Backup" button in the UI.
        var fullPath = _repository.ResolvePath(fileName);
        await _repository.WriteAllTextAsync(fullPath, JsonHelper.Serialize(root), ct)
            .ConfigureAwait(false);

        _logger.LogInformation("Persisted changes to {FileName}", fileName);
        return OperationResult.Success();
    }

    /// <summary>
    /// Attempts to parse a user-supplied value string as a JsonNode.
    /// Handles quoted strings, bare JSON (objects, arrays, numbers, booleans, null).
    /// </summary>
    private static JsonNode? ParseValueNode(string jsonValue)
    {
        try
        {
            return JsonNode.Parse(jsonValue);
        }
        catch
        {
            // If parsing fails treat the whole input as a plain string
            return JsonValue.Create(jsonValue);
        }
    }

    private static string ExtractEnvironment(string fileName)
    {
        // "appsettings.json" → "Base"
        // "appsettings.Development.json" → "Development"
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