using Microsoft.Playwright;
using System.Diagnostics;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Fixtures;

/// <summary>
/// Manages the lifetime of:
///   1. A real ASP.NET Core host (your sample app) started as a child process.
///   2. A Playwright browser instance shared across all tests in the collection.
///
/// WHY a real process instead of WebApplicationFactory:
///   WebApplicationFactory runs in-process and cannot serve embedded static assets
///   (CSS/JS) that Playwright needs to actually render the UI. A child process is
///   the only reliable way to test the full middleware pipeline end-to-end.
///
/// USAGE in xUnit:
///   Decorate each test class with [Collection("ConfigUI")]
///   and inject ConfigUITestFixture via the constructor.
/// </summary>
public sealed class ConfigUITestFixture : IAsyncLifetime
{
    // ── Configuration ─────────────────────────────────────────────────────
    // Override these with env vars or a runsettings file for CI.
    public string BaseUrl { get; private set; } = "http://localhost:5100";
    public string ConfigUIPath => $"{BaseUrl}/config-ui";
    public string? AccessToken { get; private set; }

    // ── Playwright ────────────────────────────────────────────────────────
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    public IBrowserContext BrowserContext { get; private set; } = null!;

    // ── Host process ──────────────────────────────────────────────────────
    private Process? _hostProcess;

    /// <summary>
    /// The temp directory used as the config directory for the test run.
    /// Each test that needs appsettings files writes to this directory.
    /// </summary>
    public string ConfigDirectory { get; private set; } = null!;

    // ── IAsyncLifetime ────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        BaseUrl = Environment.GetEnvironmentVariable("CONFIGUI_TEST_URL") ?? BaseUrl;
        AccessToken = Environment.GetEnvironmentVariable("CONFIGUI_ACCESS_TOKEN");

        // Create a temp directory with pre-seeded appsettings files.
        ConfigDirectory = Path.Combine(Path.GetTempPath(), "configui-uitests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ConfigDirectory);
        SeedTestConfigFiles(ConfigDirectory);

        // Start the sample app if a URL override was not provided (i.e. local dev).
        if (Environment.GetEnvironmentVariable("CONFIGUI_TEST_URL") is null)
            await StartHostProcessAsync();

        // Init Playwright
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Set to false and remove SlowMo to watch tests run live:
            //Headless = false,
            //SlowMo = 200,
        });

        BrowserContext = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });

        // Attach token to every request if configured.
        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            await BrowserContext.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["X-Config-Token"] = AccessToken
            });
        }

        // Wait for the host to be ready.
        await WaitForHostAsync();
    }

    public async Task DisposeAsync()
    {
        await BrowserContext.DisposeAsync();
        await _browser.DisposeAsync();
        _playwright.Dispose();

        _hostProcess?.Kill(entireProcessTree: true);
        _hostProcess?.Dispose();

        if (Directory.Exists(ConfigDirectory))
            Directory.Delete(ConfigDirectory, recursive: true);
    }

    // ── Page factory ──────────────────────────────────────────────────────

    /// <summary>Creates a fresh page and navigates to /config-ui.</summary>
    public async Task<IPage> NewPageAsync()
    {
        var page = await BrowserContext.NewPageAsync();
        var url = AccessToken is not null
            ? $"{ConfigUIPath}?token={Uri.EscapeDataString(AccessToken)}"
            : ConfigUIPath;
        await page.GotoAsync(url);
        // Wait for the file list to load (sidebar spinner disappears).
        await page.WaitForSelectorAsync("#file-list .file-item", new PageWaitForSelectorOptions
        {
            Timeout = 10_000
        });
        return page;
    }

    // ── Seed files ────────────────────────────────────────────────────────

    public static void SeedTestConfigFiles(string directory)
    {
        File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Information",
                  "Microsoft.AspNetCore": "Warning"
                }
              },
              "AllowedHosts": "*",
              "ConnectionStrings": {
                "Default": "Server=localhost;Database=MyDb;"
              }
            }
            """);

        File.WriteAllText(Path.Combine(directory, "appsettings.Development.json"), """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Debug"
                }
              },
              "FeatureFlags": {
                "EnableDarkMode": true,
                "MaxItems": 100
              }
            }
            """);

        File.WriteAllText(Path.Combine(directory, "appsettings.Production.json"), """
            {
              "Logging": {
                "LogLevel": {
                  "Default": "Warning"
                }
              },
              "ConnectionStrings": {
                "Default": "Server=prod-db;Database=MyProdDb;"
              }
            }
            """);
    }

    // ── Host lifecycle ────────────────────────────────────────────────────

    private Task StartHostProcessAsync()
    {
        // Resolve the sample app project. Adjust the path to match your solution layout.
        var solutionRoot = ResolveProjectRoot();
        var sampleAppPath = Path.Combine(solutionRoot, "samples", "SampleWebApp");

        if (!Directory.Exists(sampleAppPath))
            throw new InvalidOperationException(
                $"Sample app not found at '{sampleAppPath}'. " +
                "Set CONFIGUI_TEST_URL to point at a running instance instead.");

        _hostProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --no-build --urls \"{BaseUrl}\"",
                WorkingDirectory = sampleAppPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                EnvironmentVariables =
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["CONFIGUI_CONFIG_DIRECTORY"] = ConfigDirectory,
                    ["CONFIGUI_ACCESS_TOKEN"] = AccessToken ?? string.Empty,
                }
            }
        };

        _hostProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.WriteLine($"[HOST] {e.Data}");
        };
        _hostProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Console.Error.WriteLine($"[HOST ERR] {e.Data}");
        };

        _hostProcess.Start();
        _hostProcess.BeginOutputReadLine();
        _hostProcess.BeginErrorReadLine();

        return Task.CompletedTask;
    }

    private async Task WaitForHostAsync(int maxWaitMs = 30_000)
    {
        using var http = new HttpClient();
        var url = $"{BaseUrl}/config-ui";
        var sw = Stopwatch.StartNew();
        Console.WriteLine($"[Fixture] Waiting for host at {url} ...");
        while (sw.ElapsedMilliseconds < maxWaitMs)
        {
            try
            {
                var resp = await http.GetAsync(url);
                // 200, 401 (token required), or 404 (env guard) all mean the host is up.
                Console.WriteLine($"[Fixture] Host responded: {(int)resp.StatusCode}");
                    return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Fixture] Not ready yet ({ex.Message}), retrying...");
            }
            await Task.Delay(500);
        }
        throw new TimeoutException(
            $"Config UI host did not become ready within the timeout. " +
            $"Checked: {url} — make sure your app is running and CONFIGUI_TEST_URL is set correctly.");
    }

    private static string ResolveProjectRoot()
    {
        // Walk up from the test binary until we find the solution file.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

/// <summary>xUnit collection definition — ensures one fixture per test run.</summary>
[CollectionDefinition("ConfigUI")]
public sealed class ConfigUICollection : ICollectionFixture<ConfigUITestFixture> { }
