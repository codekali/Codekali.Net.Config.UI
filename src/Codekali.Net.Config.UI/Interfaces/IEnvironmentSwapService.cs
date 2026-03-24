using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Provides Move, Copy, and Compare operations across appsettings environment files.
/// </summary>
public interface IEnvironmentSwapService
{
    /// <summary>
    /// Executes the described move or copy operation.
    /// Backs up both source and target files before any modification.
    /// Returns a failure result (without touching files) if a key collision is detected
    /// and <see cref="SwapRequest.OverwriteExisting"/> is <c>false</c>.
    /// </summary>
    Task<OperationResult> ExecuteSwapAsync(SwapRequest request, CancellationToken ct = default);

    /// <summary>
    /// Compares two appsettings files and returns a structured diff highlighting
    /// keys that are missing or have differing values.
    /// </summary>
    Task<OperationResult<DiffResult>> CompareFilesAsync(string sourceFile, string targetFile, CancellationToken ct = default);

    /// <summary>
    /// Checks whether any of the specified keys already exist in the target file.
    /// Used to surface warnings in the UI before committing a swap.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> FindConflictsAsync(string targetFile, IEnumerable<string> keys, CancellationToken ct = default);
}
