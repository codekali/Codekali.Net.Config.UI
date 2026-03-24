namespace Codekali.Net.Config.UI.Models;

/// <summary>
/// A discriminated result wrapper that carries either a success value or an error message.
/// Use this instead of throwing exceptions for expected, recoverable failures.
/// </summary>
/// <typeparam name="T">The payload type on success.</typeparam>
public sealed class OperationResult<T>
{
    /// <summary>Whether the operation completed without errors.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>The result payload, available when <see cref="IsSuccess"/> is <c>true</c>.</summary>
    public T? Value { get; private init; }

    /// <summary>A human-readable error message, available when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? Error { get; private init; }

    private OperationResult() { }

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static OperationResult<T> Success(T value) =>
        new() { IsSuccess = true, Value = value };

    /// <summary>Creates a failed result with the supplied <paramref name="error"/> message.</summary>
    public static OperationResult<T> Failure(string error) =>
        new() { IsSuccess = false, Error = error };
}

/// <summary>
/// A non-generic result wrapper for void operations.
/// </summary>
public sealed class OperationResult
{
    /// <summary>Whether the operation completed without errors.</summary>
    public bool IsSuccess { get; private init; }

    /// <summary>A human-readable error message, available when <see cref="IsSuccess"/> is <c>false</c>.</summary>
    public string? Error { get; private init; }

    private OperationResult() { }

    /// <summary>Creates a successful void result.</summary>
    public static OperationResult Success() =>
        new() { IsSuccess = true };

    /// <summary>Creates a failed result with the supplied <paramref name="error"/> message.</summary>
    public static OperationResult Failure(string error) =>
        new() { IsSuccess = false, Error = error };
}
