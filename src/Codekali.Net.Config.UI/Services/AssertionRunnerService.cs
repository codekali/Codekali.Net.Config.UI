using System.Diagnostics;
using Codekali.Net.Config.UI.Interfaces;
using Codekali.Net.Config.UI.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Codekali.Net.Config.UI.Services;

/// <summary>
/// Discovers all <see cref="IConfigurationTest"/> implementations registered in DI
/// and runs them against the live <see cref="IConfiguration"/> instance.
/// </summary>
internal sealed class AssertionRunnerService : IAssertionRunnerService
{
    private readonly IEnumerable<IConfigurationTest> _tests;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AssertionRunnerService> _logger;

    public AssertionRunnerService(
        IEnumerable<IConfigurationTest> tests,
        IConfiguration configuration,
        ILogger<AssertionRunnerService> logger)
    {
        _tests = tests;
        _configuration = configuration;
        _logger = logger;
    }

    public bool HasTests => _tests.Any();

    public async Task<IReadOnlyList<AssertionResult>> RunAllAsync(CancellationToken ct = default)
    {
        var results = new List<AssertionResult>();

        foreach (var test in _tests)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var outcome = await test.RunAsync(_configuration, ct).ConfigureAwait(false);
                sw.Stop();
                results.Add(new AssertionResult
                {
                    Name = test.Name,
                    Description = test.Description,
                    Passed = outcome.Passed,
                    FailureMessage = outcome.FailureMessage,
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Configuration test '{Name}' threw an exception.", test.Name);
                results.Add(new AssertionResult
                {
                    Name = test.Name,
                    Description = test.Description,
                    Passed = false,
                    FailureMessage = $"Test threw an exception: {ex.Message}",
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
        }

        return results;
    }
}