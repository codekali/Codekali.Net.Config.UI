using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Tests the Raw JSON editor panel — Monaco load status, toggle between Tree/Raw,
/// save via Ctrl+S, format action, and invalid JSON rejection.
/// </summary>
[Collection("ConfigUI")]
public sealed class RawEditorTests(ConfigUITestFixture fixture)
{
    // ── View toggle ───────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleToRaw_ShowsRawView_HidesTreeView()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        // Start in tree mode — verify.
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

        // Tree is active by default.
        var treeClasses = await ui.TreeToggle.GetAttributeAsync("class");
        treeClasses.Should().Contain("active");

        var rawClasses = await ui.RawToggle.GetAttributeAsync("class");
        rawClasses.Should().NotContain("active");

        // Switch to raw.
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        rawClasses = await ui.RawToggle.GetAttributeAsync("class");
        rawClasses.Should().Contain("active");

        treeClasses = await ui.TreeToggle.GetAttributeAsync("class");
        treeClasses.Should().NotContain("active");
    }

    // ── Monaco load ───────────────────────────────────────────────────────

    [Fact]
    public async Task RawView_MonacoStatusBadge_IsVisible()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var badge = page.Locator("#monaco-status");
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var badgeText = await badge.InnerTextAsync();
        // Either Monaco loaded or the fallback badge is shown — both are valid UI states.
        badgeText.Should().ContainAny("Monaco", "Basic editor");
    }

    [Fact]
    public async Task RawView_ContainsFileContent()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Wait briefly for Monaco to initialise and populate the model.
        await page.WaitForTimeoutAsync(1000);

        var monacoLoaded = await page.EvaluateAsync<bool>(
            "() => !!window._monacoInstance");

        string content;
        if (monacoLoaded)
        {
            content = await ui.GetMonacoContentAsync();
        }
        else
        {
            // Fallback textarea.
            content = await ui.RawEditorFallback.InputValueAsync();
        }

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
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1000);

        // Append a new key via JS to keep the JSON valid.
        var monacoLoaded = await page.EvaluateAsync<bool>("() => !!window._monacoInstance");
        if (monacoLoaded)
        {
            var current = await ui.GetMonacoContentAsync();
            var updated = current.TrimEnd().TrimEnd('}') + ",\n  \"SavedViaRaw\": true\n}";
            await ui.SetMonacoContentAsync(updated);
        }
        else
        {
            var ta = ui.RawEditorFallback;
            var current = await ta.InputValueAsync();
            var updated = current.TrimEnd().TrimEnd('}') + ",\n  \"SavedViaRaw\": true\n}";
            await ta.FillAsync(updated);
        }

        // Click the 💾 Save button.
        await page.GetByTitle("Save (Ctrl+S)").ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 6000 });
    }

    [Fact]
    public async Task SaveRaw_InvalidJson_ShowsErrorToast()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1000);

        var monacoLoaded = await page.EvaluateAsync<bool>("() => !!window._monacoInstance");
        if (monacoLoaded)
            await ui.SetMonacoContentAsync("{ this is not valid JSON !!!}");
        else
            await ui.RawEditorFallback.FillAsync("{ this is not valid JSON !!!}");

        await page.GetByTitle("Save (Ctrl+S)").ClickAsync();

        await ui.ErrorToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    [Fact]
    public async Task CtrlS_Shortcut_TriggersSave()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1000);

        // File is valid — Ctrl+S should save without changes and show success.
        await page.Keyboard.PressAsync("Control+s");
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    // ── Format ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FormatButton_WithFallbackEditor_FormatsJson()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(1000);

        var monacoLoaded = await page.EvaluateAsync<bool>("() => !!window._monacoInstance");
        if (!monacoLoaded)
        {
            // Only relevant for the fallback textarea path.
            await ui.RawEditorFallback.FillAsync("{\"a\":1,\"b\":2}");
            await page.GetByTitle("Format JSON (Shift+Alt+F)").ClickAsync();
            var formatted = await ui.RawEditorFallback.InputValueAsync();
            formatted.Should().Contain("\n");
        }
        else
        {
            // Monaco's format action runs asynchronously — just confirm no error toast.
            await page.GetByTitle("Format JSON (Shift+Alt+F)").ClickAsync();
            await page.WaitForTimeoutAsync(800);
            var toastCount = await ui.ErrorToast.CountAsync();
            toastCount.Should().Be(0);
        }
    }

    // ── Switching files while in raw mode ─────────────────────────────────

    [Fact]
    public async Task SwitchFile_WhileInRawMode_LoadsNewFileContent()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RawToggle.ClickAsync();
        await ui.RawView.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(800);

        await ui.SelectFileAsync("appsettings.Production.json");
        await page.WaitForTimeoutAsync(800);

        var monacoLoaded = await page.EvaluateAsync<bool>("() => !!window._monacoInstance");
        string content = monacoLoaded
            ? await ui.GetMonacoContentAsync()
            : await ui.RawEditorFallback.InputValueAsync();

        // Production file contains "Warning" level, not present in base.
        content.Should().Contain("Warning");
    }
}
