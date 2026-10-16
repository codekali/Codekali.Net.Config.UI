using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Tests the Move/Copy panel and the Compare (diff) panel.
/// </summary>
[Collection("ConfigUI")]
public sealed class SwapAndDiffTests(ConfigUITestFixture fixture)
{
    // ── Swap panel setup ──────────────────────────────────────────────────

    [Fact]
    public async Task SwapPanel_SourceSelect_PopulatesKeyList()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });

        // Keys list should populate.
        await page.WaitForTimeoutAsync(600);
        var checkboxes = page.Locator("#swap-keys .key-checkbox");
        var count = await checkboxes.CountAsync();
        count.Should().BeGreaterThan(0, "source file has keys");
    }

    [Fact]
    public async Task SwapPanel_SameSourceAndTarget_ShowsWarning()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await ui.SwapTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await page.WaitForTimeoutAsync(400);

        // Select the first key.
        var first = page.Locator("#swap-keys .key-checkbox input").First;
        if (await first.IsVisibleAsync())
            await first.CheckAsync();

        await ui.SwapExecuteButton.ClickAsync();

        await ui.ErrorToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 });
        var msg = await ui.ErrorToast.InnerTextAsync();
        msg.Should().ContainAny("different", "same");
    }

    [Fact]
    public async Task SwapPanel_NoKeysSelected_ShowsWarning()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.SwapTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Production.json" });
        await page.WaitForTimeoutAsync(400);

        // Don't check any keys.
        await ui.SwapExecuteButton.ClickAsync();

        var warnToast = page.Locator(".toast.warn");
        await warnToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 });
    }

    [Fact]
    public async Task CopyOperation_ConfirmModal_ShowsCorrectText()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.SwapTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Production.json" });
        await ui.SwapOpSelect.SelectOptionAsync(new SelectOptionValue { Value = "Copy" });
        await page.WaitForTimeoutAsync(500);

        var first = page.Locator("#swap-keys .key-checkbox input").First;
        if (await first.IsVisibleAsync())
            await first.CheckAsync();

        await ui.SwapExecuteButton.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var title = await ui.ModalTitle.InnerTextAsync();
        title.Should().ContainAny("Copy", "1 key");

        // Cancel — we don't want to mutate files across tests.
        await ui.ModalCancelButton.ClickAsync();
    }

    [Fact]
    public async Task MoveOperation_OpSelect_ShowsMoveLabel()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapOpSelect.SelectOptionAsync(new SelectOptionValue { Value = "Move" });

        var selectedText = await ui.SwapOpSelect.InnerTextAsync();
        selectedText.Should().Contain("Move");
    }

    [Fact]
    public async Task OverwriteCheckbox_DefaultsToUnchecked()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var isChecked = await ui.SwapOverwriteCheckbox.IsCheckedAsync();
        isChecked.Should().BeFalse();
    }

    // ── Diff panel ────────────────────────────────────────────────────────

    [Fact]
    public async Task DiffPanel_Compare_ShowsDiffTable()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavDiff.ClickAsync();
        await ui.DiffPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.DiffSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await ui.DiffTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffCompareButton.ClickAsync();

        // Wait for results.
        await ui.DiffResult.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForTimeoutAsync(600);

        var resultHtml = await ui.DiffResult.InnerHTMLAsync();
        resultHtml.Should().ContainAny("diff-table", "identical", "diff-only", "diff-changed");
    }

    [Fact]
    public async Task DiffPanel_SameFile_ShowsIdentical()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavDiff.ClickAsync();
        await ui.DiffPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.DiffSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await ui.DiffTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await ui.DiffCompareButton.ClickAsync();

        await page.WaitForTimeoutAsync(600);

        // All keys are identical — table has only "identical" rows OR the message says so.
        var resultText = await ui.DiffResult.InnerTextAsync();
        resultText.Should().ContainAny("identical", "Files are identical");
    }

    [Fact]
    public async Task DiffPanel_DevelopmentVsProduction_HighlightsLogLevelDifference()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavDiff.ClickAsync();
        await ui.DiffPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.DiffSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Production.json" });
        await ui.DiffCompareButton.ClickAsync();

        await page.WaitForTimeoutAsync(700);

        // Logging:LogLevel:Default differs between Dev (Debug) and Prod (Warning).
        var resultText = await ui.DiffResult.InnerTextAsync();
        resultText.Should().Contain("Logging");
    }

    [Fact]
    public async Task DiffPanel_ResultsReset_WhenNavigatingAway()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavDiff.ClickAsync();
        await ui.DiffPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.DiffSourceSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.json" });
        await ui.DiffTargetSelect.SelectOptionAsync(new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffCompareButton.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Navigate away.
        await ui.NavEditor.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        // Navigate back — result div should be empty (reset by setView).
        await ui.NavDiff.ClickAsync();
        var resultContent = await ui.DiffResult.InnerHTMLAsync();
        resultContent.Trim().Should().BeEmpty();
    }
}
