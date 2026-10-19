using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests;

[Collection("ConfigUI")]
public sealed class SwapAndDiffTests(ConfigUITestFixture fixture)
{
    // ── Swap panel ────────────────────────────────────────────────────────

    [Fact]
    public async Task SwapPanel_SourceSelect_PopulatesKeyList()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });

        await page.WaitForTimeoutAsync(600);
        var checkboxes = page.Locator("#swap-keys .key-checkbox");
        var count = await checkboxes.CountAsync();
        count.Should().BeGreaterThan(0);
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

        var first = page.Locator("#swap-keys .key-checkbox input").First;
        if (await first.IsVisibleAsync())
            await first.CheckAsync();

        await ui.SwapExecuteButton.ClickAsync();

        // JS emits a WARN toast when source === target, not an error toast.
        var warnToast = page.Locator(".toast.warn");
        await warnToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 });
        var msg = await warnToast.InnerTextAsync();
        msg.Should().ContainAny("different", "same", "Source");
    }

    [Fact]
    public async Task SwapPanel_NoKeysSelected_ShowsWarning()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavSwap.ClickAsync();
        await ui.SwapPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.SwapSourceSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.SwapTargetSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Production.json" });
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

        await ui.SwapSourceSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.SwapTargetSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Production.json" });
        await ui.SwapOpSelect.SelectOptionAsync(new SelectOptionValue { Value = "Copy" });
        await page.WaitForTimeoutAsync(500);

        var first = page.Locator("#swap-keys .key-checkbox input").First;
        if (await first.IsVisibleAsync())
            await first.CheckAsync();

        await ui.SwapExecuteButton.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var title = await ui.ModalTitle.InnerTextAsync();
        title.Should().ContainAny("Copy", "key");

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
        await ui.DiffTargetSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffCompareButton.ClickAsync();

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

        await ui.DiffSourceSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffTargetSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Production.json" });
        await ui.DiffCompareButton.ClickAsync();

        await page.WaitForTimeoutAsync(700);

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
        await ui.DiffTargetSelect.SelectOptionAsync(
            new SelectOptionValue { Label = "appsettings.Development.json" });
        await ui.DiffCompareButton.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        await ui.NavEditor.ClickAsync();
        await page.WaitForTimeoutAsync(200);

        await ui.NavDiff.ClickAsync();
        var resultContent = await ui.DiffResult.InnerHTMLAsync();
        resultContent.Trim().Should().BeEmpty();
    }
}