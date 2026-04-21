using Codekali.Net.Config.UI.Models;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>Discovers and runs all registered <see cref="IConfigurationTest"/> implementations.</summary>
public interface IAssertionRunnerService
{
    /// <summary>Returns true when at least one <see cref="IConfigurationTest"/> is registered.</summary>
    bool HasTests { get; }

    /// <summary>Runs all registered tests and returns their results.</summary>
    Task<IReadOnlyList<AssertionResult>> RunAllAsync(CancellationToken ct = default);
}