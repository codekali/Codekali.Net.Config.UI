using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests;

[Collection("ConfigUI")]
public sealed class CrudOperationTests(ConfigUITestFixture fixture)
{
    // ── Add ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddKey_ScalarString_AppearsInTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("NewFeature:Name", "\"TestValue\"");

        await ui.TreeRowByKey("NewFeature").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    [Fact]
    public async Task AddKey_NestedPath_CreatesIntermediateObjects()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("Database:Host", "\"localhost\"");

        await ui.TreeRowByKey("Database").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        await ui.ExpandToggleForKey("Database").ClickAsync();
        await ui.TreeRowByKey("Host").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    [Fact]
    public async Task AddKey_DuplicatePath_ShowsErrorToast()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // "Logging:LogLevel:Default" already exists in the seeded file.
        await ui.AddKeyButton.ClickAsync();
        await ui.AddForm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.AddKeyInput.FillAsync("Logging:LogLevel:Default");
        await ui.AddValueInput.FillAsync("\"Debug\"");
        await ui.AddSubmitButton.ClickAsync();

        await ui.ErrorToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    [Fact]
    public async Task AddKey_CancelButton_HidesForm()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.AddKeyButton.ClickAsync();
        await ui.AddForm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.AddCancelButton.ClickAsync();

        await ui.AddForm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
    }

    [Fact]
    public async Task AddKey_BooleanValue_AppearsWithCorrectType()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("FeatureFlags:EnableExport", "true");

        // Expand FeatureFlags.
        var toggle = ui.ExpandToggleForKey("FeatureFlags");
        if (await toggle.InnerTextAsync() == "▸")
            await toggle.ClickAsync();

        var valueEl = page.Locator(".tree-value.boolean");
        await valueEl.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    // ── Update (inline editor) ────────────────────────────────────────────

    [Fact]
    public async Task EditKey_InlineEditor_OpensOnDoubleClick()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        var row = ui.TreeRowByKey("AllowedHosts");
        await row.DblClickAsync();

        await ui.InlineEditor.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    [Fact]
    public async Task EditKey_PressingEscape_CancelsWithoutSaving()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        var row = ui.TreeRowByKey("AllowedHosts");
        await row.DblClickAsync();
        await ui.InlineEditor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.InlineInput.FillAsync("changed-but-cancelled");
        await ui.InlineInput.PressAsync("Escape");

        await ui.InlineEditor.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });
        var toastCount = await ui.Toasts.CountAsync();
        toastCount.Should().Be(0);
    }

    [Fact]
    public async Task EditKey_PressingEnter_SavesAndShowsSuccessToast()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // MaxItems is nested under FeatureFlags — expand first.
        var featureFlagsToggle = ui.ExpandToggleForKey("FeatureFlags");
        await featureFlagsToggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        if ((await featureFlagsToggle.InnerTextAsync()).Trim() == "▸")
            await featureFlagsToggle.ClickAsync();

        // Now MaxItems is visible.
        var row = ui.TreeRowByKey("MaxItems");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await row.DblClickAsync();
        await ui.InlineInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.InlineInput.FillAsync("200");
        await ui.InlineInput.PressAsync("Enter");

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Value updated.
        var valueEl = page.Locator(".tree-value.number", new PageLocatorOptions { HasText = "200" }).First;
        await valueEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task AutoSaveToggle_WhenOff_DoesNotSaveOnBlur()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        var toggle = page.Locator("#blur-save-toggle");
        await toggle.ClickAsync();
        (await toggle.GetAttributeAsync("aria-checked")).Should().Be("false");

        var row = ui.TreeRowByKey("AllowedHosts");
        await row.DblClickAsync();
        await ui.InlineInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.InlineInput.FillAsync("blur-no-save");

        await ui.ToolbarTitle.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var toastCount = await ui.Toasts.CountAsync();
        toastCount.Should().Be(0, "auto-save is off, blur should not trigger a save");
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteKey_ConfirmModal_RemovesKeyFromTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Add a disposable key first so the seeded file isn't touched.
        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("ToBeDeleted", "\"bye\"");
        await ui.TreeRowByKey("ToBeDeleted").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        // Delete it.
        var row = ui.TreeRowByKey("ToBeDeleted");
        await row.HoverAsync();
        await row.Locator(".row-actions .btn-danger").ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Wait for the tree to re-render, then assert gone.
        await page.WaitForTimeoutAsync(500);
        var count = await ui.TreeRowByKey("ToBeDeleted").CountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task DeleteKey_CancelModal_KeyRemainsInTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        var row = ui.TreeRowByKey("Logging");
        await row.HoverAsync();
        await row.Locator(".row-actions .btn-danger").ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalCancelButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });

        await ui.TreeRowByKey("Logging").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    // ── Tree expand / collapse ────────────────────────────────────────────

    [Fact]
    public async Task TreeRow_ExpandCollapse_TogglesChildren()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        var toggle = ui.ExpandToggleForKey("Logging");
        await toggle.ClickAsync();

        await ui.TreeRowByKey("LogLevel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        await toggle.ClickAsync();
        await ui.TreeRowByKey("LogLevel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });
    }

    // ── Search ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_FiltersTreeToMatchingKeys()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.SearchInput.FillAsync("connection");

        await ui.TreeRowByKey("ConnectionStrings").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 3000
        });

        await ui.TreeRowByKey("AllowedHosts").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 3000
        });
    }

    [Fact]
    public async Task Search_ClearingFilter_RestoresFullTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.SearchInput.FillAsync("connection");
        await ui.SearchInput.ClearAsync();

        await ui.TreeRowByKey("AllowedHosts").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 3000
        });
    }

    // ── Sensitive value masking ───────────────────────────────────────────

    [Fact]
    public async Task SensitiveKey_IsMaskedByDefault()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("Database:Password", "\"super-secret-123\"");

        // Expand Database.
        await ui.ExpandToggleForKey("Database").ClickAsync();

        var maskedEl = page.Locator(".tree-value.masked");
        await maskedEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var revealBtn = page.Locator("button", new PageLocatorOptions { HasText = "reveal" }).First;
        await revealBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }
}