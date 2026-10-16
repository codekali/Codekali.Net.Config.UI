using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Verifies sidebar file listing, nav switching, and the no-file landing state.
/// These are the first things a new design can break — run them first.
/// </summary>
[Collection("ConfigUI")]
public sealed class SidebarAndNavigationTests(ConfigUITestFixture fixture)
{
    [Fact]
    public async Task Sidebar_ShowsAllThreeSeededFiles()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        var items = await ui.FileItems.AllAsync();
        items.Should().HaveCount(3);

        var texts = await Task.WhenAll(items.Select(i => i.InnerTextAsync()));
        var allText = string.Join(" ", texts);
        allText.Should().Contain("appsettings.json");
        allText.Should().Contain("appsettings.Development.json");
        allText.Should().Contain("appsettings.Production.json");
    }

    [Fact]
    public async Task Sidebar_EnvironmentPills_CorrectLabels()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Base pill
        var basePill = page.Locator(".file-env-pill.base");
        await basePill.WaitForAsync();
        (await basePill.InnerTextAsync()).Should().Contain("Base");

        // Dev pill
        var devPill = page.Locator(".file-env-pill.dev");
        (await devPill.InnerTextAsync()).Should().Contain("Deve");

        // Prod pill
        var prodPill = page.Locator(".file-env-pill.prod");
        (await prodPill.InnerTextAsync()).Should().Contain("Prod");
    }

    [Fact]
    public async Task NoFileState_ShownOnInitialLoad()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NoFileState.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        (await ui.NoFileTitle.InnerTextAsync()).Should().Be("No file selected");

        // Toolbar rows must be hidden before a file is selected.
        await ui.ToolbarRow1.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        await ui.ToolbarRow2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [Fact]
    public async Task SelectingFile_ShowsToolbarAndHidesNoFileState()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        // No-file state disappears.
        await ui.NoFileState.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        // Both toolbar rows appear.
        await ui.ToolbarRow1.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ToolbarRow2.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Toolbar title contains the file name.
        var title = await ui.ToolbarTitle.InnerTextAsync();
        title.Should().Contain("appsettings.json");
    }

    [Fact]
    public async Task SelectingFile_MarksItActiveInSidebar()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        var active = await ui.ActiveFileItem.InnerTextAsync();
        active.Should().Contain("appsettings.Development.json");
    }

    [Theory]
    [InlineData("swap", "#view-swap")]
    [InlineData("diff", "#view-diff")]
    [InlineData("guide", "#view-guide")]
    [InlineData("tests", "#view-tests")]
    public async Task NavItems_SwitchView(string navId, string panelSelector)
    {
        var page = await fixture.NewPageAsync();

        await page.Locator($"#nav-{navId}").ClickAsync();

        // Target panel becomes visible.
        await page.Locator(panelSelector).WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        // Editor panel is hidden.
        await page.Locator("#view-editor").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });

        // Active class on the clicked nav item.
        var navItem = page.Locator($"#nav-{navId}");
        var classes = await navItem.GetAttributeAsync("class");
        classes.Should().Contain("active");
    }

    [Fact]
    public async Task GuidePanel_ContainsBestPracticesContent()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavGuide.ClickAsync();
        await ui.GuidePanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var text = await ui.GuidePanel.InnerTextAsync();
        text.Should().Contain("IOptionsSnapshot");
        text.Should().Contain("IOptionsMonitor");
        text.Should().Contain("Best Practices");
    }

    [Fact]
    public async Task ThemeToggle_SwitchesToLightAndBack()
    {
        var page = await fixture.NewPageAsync();

        var htmlTheme = await page.EvaluateAsync<string>(
            "() => document.documentElement.getAttribute('data-theme')");
        htmlTheme.Should().Be("dark");

        await page.GetByTitle("Toggle theme").ClickAsync();

        htmlTheme = await page.EvaluateAsync<string>(
            "() => document.documentElement.getAttribute('data-theme')");
        htmlTheme.Should().Be("light");

        // Toggle back.
        await page.GetByTitle("Toggle theme").ClickAsync();
        htmlTheme = await page.EvaluateAsync<string>(
            "() => document.documentElement.getAttribute('data-theme')");
        htmlTheme.Should().Be("dark");
    }
}
