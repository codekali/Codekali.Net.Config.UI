using Microsoft.Playwright;
using System.Diagnostics;
using Xunit;

namespace Codekali.Net.Config.UI.UITests;

/// <summary>
/// Manages a single Playwright browser context shared across all tests in the collection.
///
/// KEY DESIGN DECISIONS:
///   1. ONE page is created per test via NewPageAsync() — but the page is disposed
///      after each test, keeping a single browser instance alive. This avoids the
///      "new tab per test" problem while still isolating browser state.
///   2. Config files are RESET before each page creation so tests never corrupt
///      each other through shared file state.
///   3. The fixture starts the sample app if CONFIGUI_TEST_URL is not set.
/// </summary>
public sealed class ConfigUITestFixture : IAsyncLifetime
{
    // ── Configuration ─────────────────────────────────────────────────────
    public string BaseUrl { get; private set; } = "http://localhost:5100";
    public string ConfigUIPath => $"{BaseUrl}/config-ui";
    public string? AccessToken { get; private set; }

    // ── Playwright ────────────────────────────────────────────────────────
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    // ── Host process ──────────────────────────────────────────────────────
    private Process? _hostProcess;

    /// <summary>The directory the sample app reads appsettings files from.</summary>
    public string ConfigDirectory { get; private set; } = null!;

    // ── IAsyncLifetime ────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        BaseUrl = Environment.GetEnvironmentVariable("CONFIGUI_TEST_URL") ?? BaseUrl;
        AccessToken = Environment.GetEnvironmentVariable("CONFIGUI_ACCESS_TOKEN");

        // Resolve the config directory — either the sample app's own directory
        // (when we start the process ourselves) or a temp directory.
        ConfigDirectory = ResolveSampleAppDirectory()
            ?? Path.Combine(Path.GetTempPath(), "configui-uitests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(ConfigDirectory);

        // Seed once so the host can start up with valid files.
        ResetConfigFiles();

        if (Environment.GetEnvironmentVariable("CONFIGUI_TEST_URL") is null)
            await StartHostProcessAsync();

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // Change to false + SlowMo = 300 to watch tests run visually:
            // Headless = false,
            // SlowMo = 300,
        });

        await WaitForHostAsync();
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();

        _hostProcess?.Kill(entireProcessTree: true);
        _hostProcess?.Dispose();
    }

    // ── Page factory ──────────────────────────────────────────────────────

    /// <summary>
    /// Resets config files to a known clean state, creates a fresh browser context
    /// and page, then navigates to /config-ui.
    ///
    /// Each test gets a clean slate — no leftover keys, no accumulated .bak files
    /// from prior tests, and a brand-new browser context (no cookies/localStorage).
    /// </summary>
    public async Task<IPage> NewPageAsync()
    {
        // Reset files BEFORE opening the page so the sidebar loads the clean state.
        ResetConfigFiles();

        IBrowserContext context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
        });

        if (!string.IsNullOrWhiteSpace(AccessToken))
        {
            await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["X-Config-Token"] = AccessToken
            });
        }

        var page = await context.NewPageAsync();
        var url = AccessToken is not null
            ? $"{ConfigUIPath}?token={Uri.EscapeDataString(AccessToken)}"
            : ConfigUIPath;

        await page.GotoAsync(url);

        // Wait for the file list to finish loading.
        await page.WaitForSelectorAsync("#file-list .file-item", new PageWaitForSelectorOptions
        {
            Timeout = 10_000
        });

        return page;
    }

    // ── File management ───────────────────────────────────────────────────

    /// <summary>
    /// Writes the three canonical test appsettings files and deletes all
    /// accumulated .bak and .audit.json files from prior test runs.
    /// Called before every test via NewPageAsync().
    /// </summary>
    public void ResetConfigFiles()
    {
        // Delete all .bak files so the backup count tests start clean.
        foreach (string bak in Directory.GetFiles(ConfigDirectory, "*.bak"))
            File.Delete(bak);

        // Delete audit files so audit tests start from zero.
        foreach (string audit in Directory.GetFiles(ConfigDirectory, "*.audit.json"))
            File.Delete(audit);

        // Write the three canonical files.
        File.WriteAllText(Path.Combine(ConfigDirectory, "appsettings.json"), """
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

        File.WriteAllText(Path.Combine(ConfigDirectory, "appsettings.Development.json"), """
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

        File.WriteAllText(Path.Combine(ConfigDirectory, "appsettings.Production.json"), """
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
        var sampleAppPath = ResolveSampleAppDirectory();
        if (sampleAppPath is null)
            throw new InvalidOperationException(
                "Sample app not found. Set CONFIGUI_TEST_URL to point at a running instance.");

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
                    // Point the library at our controlled test directory.
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
            $"Config UI host did not become ready within the timeout. Checked: {url}");
    }

    private string? ResolveSampleAppDirectory()
    {
        // Walk up from the test binary to the solution root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;

        if (dir is null) return null;

        var sampleApp = Path.Combine(dir.FullName, "samples", "SampleWebApp");
        return Directory.Exists(sampleApp) ? sampleApp : null;
    }
}

/// <summary>xUnit collection definition — one fixture instance per test run.</summary>
[CollectionDefinition("ConfigUI")]
public sealed class ConfigUICollection : ICollectionFixture<ConfigUITestFixture> { }