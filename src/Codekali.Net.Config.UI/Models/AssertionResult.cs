using Codekali.Net.Config.UI.Interfaces;

namespace Codekali.Net.Config.UI.Models;

/// <summary>The result of running a single <see cref="IConfigurationTest"/>.</summary>
public sealed class AssertionResult
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Passed { get; init; }
    public string? FailureMessage { get; init; }
    public long ElapsedMs { get; init; }
}