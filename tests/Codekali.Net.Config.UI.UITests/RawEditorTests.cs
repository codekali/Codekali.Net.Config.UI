using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests;

/// <summary>
/// Tests the Raw JSON editor panel.
/// NOTE: Monaco always loads from CDN in these tests, so the fallback textarea
/// (#raw-editor-ta) is always hidden. All content read/write goes through the
/// Monaco JS API (window._monacoInstance).
/// </summary>
[Collection("ConfigUI")]
public sealed class RawEditorTests(ConfigUITestFixture fixture)
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Switches to raw mode and waits for Monaco to initialise.</summary>
    private static async Task SwitchToRawAndWaitAsync(IPage page, ConfigUIPage ui)
    {
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // Give Monaco up to 3 s to finish mounting.
        await page.WaitForFunctionAsync(
            "() => !!window._monacoInstance && window._monacoInstance.getValue().length > 0",
            null,
            new PageWaitForFunctionOptions { Timeout = 3000 });
    }

    // ── View toggle ───────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleToRaw_ShowsRawView_HidesTreeView()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.TreeView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.RawToggle.ClickAsync();

        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.TreeView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [Fact]
    public async Task ToggleBackToTree_HidesRawView_ShowsTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.TreeToggle.ClickAsync();

        await ui.TreeView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [Fact]
    public async Task RawView_ToggleButtonsReflectActiveMode()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        var treeClasses = await ui.TreeToggle.GetAttributeAsync("class");
        treeClasses.Should().Contain("active");

        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var rawClasses = await ui.RawToggle.GetAttributeAsync("class");
        rawClasses.Should().Contain("active");

        treeClasses = await ui.TreeToggle.GetAttributeAsync("class");
        treeClasses.Should().NotContain("active");
    }

    // ── Monaco status ─────────────────────────────────────────────────────

    [Fact]
    public async Task RawView_MonacoStatusBadge_IsVisible()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1000);

        var badge = page.Locator("#monaco-status");
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var badgeText = await badge.InnerTextAsync();
        badgeText.Should().ContainAny("Monaco", "Basic editor");
    }

    [Fact]
    public async Task RawView_ContainsFileContent()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await SwitchToRawAndWaitAsync(page, ui);

        var content = await ui.GetMonacoContentAsync();
        content.Should().Contain("AllowedHosts");
        content.Should().Contain("Logging");
    }

    // ── Save ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveRaw_ValidJson_ShowsSuccessToast()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await SwitchToRawAndWaitAsync(page, ui);

        var current = await ui.GetMonacoContentAsync();
        var updated = current.TrimEnd().TrimEnd('}') + ",\n  \"SavedViaRaw\": true\n}";
        await ui.SetMonacoContentAsync(updated);

        await page.GetByTitle("Save (Ctrl+S)").ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 6000 });
    }

    [Fact]
    public async Task SaveRaw_InvalidJson_ShowsErrorToast()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await SwitchToRawAndWaitAsync(page, ui);

        await ui.SetMonacoContentAsync("{ this is not valid JSON !!!}");

        await page.GetByTitle("Save (Ctrl+S)").ClickAsync();
        await ui.ErrorToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    [Fact]
    public async Task CtrlS_Shortcut_TriggersSave()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await SwitchToRawAndWaitAsync(page, ui);

        // Focus the Monaco editor then send Ctrl+S.
        await ui.MonacoContainer.ClickAsync();
        await page.Keyboard.PressAsync("Control+s");
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    // ── Format ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FormatButton_DoesNotThrowError()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await SwitchToRawAndWaitAsync(page, ui);

        // Click the Format button — Monaco's format-document action runs.
        await page.GetByTitle("Format JSON (Shift+Alt+F)").ClickAsync();
        await page.WaitForTimeoutAsync(600);

        // No error toast should appear.
        var errorCount = await ui.ErrorToast.CountAsync();
        errorCount.Should().Be(0);
    }

    // ── Switching files ───────────────────────────────────────────────────

    [Fact]
    public async Task SwitchFile_WhileInRawMode_LoadsNewFileContent()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await SwitchToRawAndWaitAsync(page, ui);

        // Switch to Production file (raw mode is preserved).
        // We click the sidebar item directly — no tree-view wait needed in raw mode.
        await page.Locator("#file-list .file-item", new PageLocatorOptions
        {
            HasText = "appsettings.Production.json"
        }).First.ClickAsync();

        // Wait for Monaco to reload with the new file's content.
        await page.WaitForFunctionAsync(
            "() => window._monacoInstance?.getValue().includes('Warning')",
            null,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var content = await ui.GetMonacoContentAsync();
        content.Should().Contain("Warning");
    }
}