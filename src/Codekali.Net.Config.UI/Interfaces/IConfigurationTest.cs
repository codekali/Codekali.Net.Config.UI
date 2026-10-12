using Microsoft.Extensions.Configuration;

namespace Codekali.Net.Config.UI.Interfaces;

/// <summary>
/// Implement this interface in your project to define configuration assertions.
/// All implementations discovered in the host application's DI container are
/// surfaced in the Config UI Test Runner panel.
/// </summary>
/// <example>
/// <code>
/// public class AppConfigTests : IConfigurationTest
/// {
///     public string Name => "Production Readiness";
///
///     public Task&lt;AssertionOutcome&gt; RunAsync(IConfiguration config, CancellationToken ct)
///     {
///         if (string.IsNullOrWhiteSpace(config["ConnectionStrings:Default"]))
///             return Task.FromResult(AssertionOutcome.Fail("ConnectionStrings:Default must not be empty in Production."));
///
///         return Task.FromResult(AssertionOutcome.Pass());
///     }
/// }
/// </code>
/// </example>
public interface IConfigurationTest
{
    /// <summary>Display name shown in the Test Runner panel.</summary>
    string Name { get; }

    /// <summary>Optional description shown as a subtitle in the panel.</summary>
    string? Description => null;

    /// <summary>Executes the assertion against the provided <see cref="IConfiguration"/> snapshot.</summary>
    Task<AssertionOutcome> RunAsync(IConfiguration config, CancellationToken ct = default);
}

/// <summary>The result of a single <see cref="IConfigurationTest"/> run.</summary>
public sealed class AssertionOutcome
{
    public bool Passed { get; private init; }
    public string? FailureMessage { get; private init; }

    private AssertionOutcome() { }

    public static AssertionOutcome Pass() => new() { Passed = true };
    public static AssertionOutcome Fail(string message) => new() { Passed = false, FailureMessage = message };
}