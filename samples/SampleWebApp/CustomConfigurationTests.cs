using Codekali.Net.Config.UI.Interfaces;

namespace SampleWebApp;
public class CustomConfigurationTests : IConfigurationTest
{
    public string Name => "Production Readiness";

    public Task<AssertionOutcome> RunAsync(IConfiguration config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config["ConnectionStrings:Default"]))
            return Task.FromResult(AssertionOutcome.Fail("ConnectionStrings:Default must not be empty in Production."));

        if (string.IsNullOrWhiteSpace(config["ExternalServices:PaymentGateway:ApiKey"]))
            return Task.FromResult(AssertionOutcome.Fail("API KEY :Default must not be empty in Production."));
    
        return Task.FromResult(AssertionOutcome.Pass());
    }
}