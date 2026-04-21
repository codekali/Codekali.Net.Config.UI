using System.Text.Json;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Writes audit entries to a <c>{fileName}.audit.json</c> file
/// alongside each appsettings file. Thread-safe via a per-file SemaphoreSlim.
/// </summary>
internal sealed class AuditService(
    IConfigFileRepository repository,
    ConfigUIOptions options,
    ILogger<AuditService> logger) : IAuditService
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };
    // One semaphore per audit file path to prevent concurrent write corruption.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim>
        _locks = new();

    public async Task RecordAsync(string fileName, AuditOperation operation,
        string keyPath, string? oldValue, string? newValue, CancellationToken ct = default)
    {
        if (!options.EnableAuditLogging) return;

        var entry = new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Operation = operation,
            KeyPath = keyPath,
            OldValue = oldValue,
            NewValue = newValue,
            Environment = ExtractEnvironment(fileName),
        };

        if (options.ForwardAuditToLogger)
            logger.LogInformation("[Audit] {Op} {Key} in {File} — old={Old} new={New}",
                operation, keyPath, fileName, oldValue, newValue);

        var auditPath = repository.ResolvePath(fileName + ".audit.json");
        var sem = _locks.GetOrAdd(auditPath, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            List<AuditEntry> entries = [];
            if (repository.FileExists(auditPath))
            {
                var raw = await repository.ReadAllTextAsync(auditPath, ct).ConfigureAwait(false);
                try { entries = JsonSerializer.Deserialize<List<AuditEntry>>(raw, _opts) ?? []; }
                catch { /* corrupt file — start fresh */ }
            }
            entries.Insert(0, entry); // newest first
            await repository.WriteAllTextAsync(auditPath,
                JsonSerializer.Serialize(entries, _opts), ct).ConfigureAwait(false);
        }
        finally { sem.Release(); }
    }

    public async Task<OperationResult<IReadOnlyList<AuditEntry>>> GetEntriesAsync(
        string fileName, CancellationToken ct = default)
    {
        var auditPath = repository.ResolvePath(fileName + ".audit.json");
        if (!repository.FileExists(auditPath))
            return OperationResult<IReadOnlyList<AuditEntry>>.Success([]);
        try
        {
            var raw = await repository.ReadAllTextAsync(auditPath, ct).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<AuditEntry>>(raw, _opts) ?? [];
            return OperationResult<IReadOnlyList<AuditEntry>>.Success(entries);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<AuditEntry>>.Failure(ex.Message);
        }
    }

    private static string ExtractEnvironment(string fileName)
    {
        const string prefix = "appsettings.";
        const string suffix = ".json";
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "Unknown";
        var middle = fileName[prefix.Length..];
        if (middle.Equals("json", StringComparison.OrdinalIgnoreCase)) return "Base";
        return middle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? middle[..^suffix.Length] : middle;
    }
}