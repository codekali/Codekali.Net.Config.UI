using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Tests every CRUD operation exposed in the tree view:
///   Add key, Update key (inline editor), Delete key, nested paths.
/// Each test re-seeds the config directory so they are fully independent.
/// </summary>
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

        // Re-render has happened (toast triggered reload). Key must be visible.
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

        // Expand the Database object.
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
        var msg = await ui.ErrorToast.InnerTextAsync();
        msg.Should().ContainAny("exists", "already");
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

        // Expand FeatureFlags if collapsed.
        var row = ui.TreeRowByKey("FeatureFlags");
        var toggle = row.Locator(".tree-toggle");
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

        // AllowedHosts is a top-level scalar.
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

        // Editor closes, no toast.
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

        // Edit MaxItems (number).
        var row = ui.TreeRowByKey("MaxItems");
        if (await row.IsHiddenAsync())
        {
            await ui.ExpandToggleForKey("FeatureFlags").ClickAsync();
            row = ui.TreeRowByKey("MaxItems");
        }

        await row.DblClickAsync();
        await ui.InlineInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.InlineInput.FillAsync("200");
        await ui.InlineInput.PressAsync("Enter");

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Value updated in the tree.
        var valueEl = page.Locator(".tree-value.number", new PageLocatorOptions { HasText = "200" }).First;
        await valueEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task AutoSaveToggle_WhenOff_DoesNotSaveOnBlur()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        // Turn auto-save OFF.
        var toggle = page.Locator("#blur-save-toggle");
        await toggle.ClickAsync();
        var state = await toggle.GetAttributeAsync("aria-checked");
        state.Should().Be("false");

        var row = ui.TreeRowByKey("AllowedHosts");
        await row.DblClickAsync();
        await ui.InlineInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.InlineInput.FillAsync("blur-no-save");

        // Click elsewhere to blur.
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

        // First add a key we can safely delete.
        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("ToBeDeleted", "\"bye\"");
        await ui.TreeRowByKey("ToBeDeleted").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        // Now delete it.
        await ui.DeleteKeyAsync("ToBeDeleted");

        // Row must disappear.
        await ui.TreeRowByKey("ToBeDeleted").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });
    }

    [Fact]
    public async Task DeleteKey_CancelModal_KeyRemainsInTree()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // Hover the Logging row to reveal actions.
        var row = ui.TreeRowByKey("Logging");
        await row.HoverAsync();
        await row.Locator(".row-actions .btn-danger").ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalCancelButton.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        // Logging key still present.
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

        // Children appear.
        await ui.TreeRowByKey("LogLevel").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        // Collapse.
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

        // ConnectionStrings key visible.
        await ui.TreeRowByKey("ConnectionStrings").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 3000
        });

        // AllowedHosts key filtered out.
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

        // Add a password key.
        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("Database:Password", "\"super-secret-123\"");

        // Expand Database.
        await ui.ExpandToggleForKey("Database").ClickAsync();

        var maskedEl = page.Locator(".tree-value.masked");
        await maskedEl.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Reveal button exists.
        var revealBtn = page.Locator("button", new PageLocatorOptions { HasText = "reveal" }).First;
        await revealBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }
}