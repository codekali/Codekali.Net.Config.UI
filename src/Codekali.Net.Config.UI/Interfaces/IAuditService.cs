using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Appends write operations to a per-file append-only audit log.
/// Only active when <see cref="ConfigUIOptions.EnableAuditLogging"/> is <c>true</c>.
/// </summary>
public interface IAuditService
{
    /// <summary>Records a write operation against <paramref name="fileName"/>.</summary>
    Task RecordAsync(string fileName, AuditOperation operation,
        string keyPath, string? oldValue, string? newValue,
        CancellationToken ct = default);

    /// <summary>Returns all audit entries for <paramref name="fileName"/>, newest-first.</summary>
    Task<OperationResult<IReadOnlyList<AuditEntry>>> GetEntriesAsync(
        string fileName, CancellationToken ct = default);
}