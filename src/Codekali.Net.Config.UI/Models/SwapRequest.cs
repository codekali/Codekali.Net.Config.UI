namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// Describes an operation that moves or copies one or more configuration keys
/// from a source appsettings file to a target appsettings file.
/// </summary>
public sealed class SwapRequest
{
    /// <summary>File name of the source appsettings file.</summary>
    public string SourceFile { get; init; } = string.Empty;

    /// <summary>File name of the target appsettings file.</summary>
    public string TargetFile { get; init; } = string.Empty;

    /// <summary>
    /// The dot-notation keys to move or copy, e.g. ["ConnectionStrings", "Logging:LogLevel:Default"].
    /// </summary>
    public List<string> Keys { get; init; } = [];

    /// <summary>
    /// The operation type: <see cref="SwapOperation.Move"/> removes the key from the source
    /// after copying; <see cref="SwapOperation.Copy"/> leaves the source unchanged.
    /// </summary>
    public SwapOperation Operation { get; init; } = SwapOperation.Copy;

    /// <summary>When true, silently overwrites a key that already exists in the target file.</summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>The type of environment swap operation to perform.</summary>
public enum SwapOperation
{
    /// <summary>Copy the key to the target file and remove it from the source.</summary>
    Move,
    /// <summary>Copy the key to the target file, leaving the source unchanged.</summary>
    Copy
}
