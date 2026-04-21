namespace Codekali.Net.Config.UI.Models;

/// <summary>A single entry in the per-file change audit log.</summary>
public sealed class AuditEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public AuditOperation Operation { get; init; }
    public string KeyPath { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string Environment { get; init; } = string.Empty;
}

/// <summary>The type of write operation that was audited.</summary>
public enum AuditOperation { Add, Update, Delete, Restore, SaveRaw, AppendArrayItem, RemoveArrayItem }