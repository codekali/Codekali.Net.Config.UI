using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Tests the right sidebar: Backup tab (create, diff, restore, delete) and Audit tab.
/// </summary>
[Collection("ConfigUI")]
public sealed class BackupAndAuditTests(ConfigUITestFixture fixture)
{
    // ── Sidebar open / close ──────────────────────────────────────────────

    [Fact]
    public async Task BackupsButton_NoFileSelected_ShowsWarning()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Do NOT select a file.
        await ui.BackupsButton.ClickAsync();

        var warnToast = page.Locator(".toast.warn");
        await warnToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 });
        (await warnToast.InnerTextAsync()).Should().Contain("Select a file");
    }

    [Fact]
    public async Task BackupsButton_WithFileSelected_OpensSidebar()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();

        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 4000
        });
    }

    [Fact]
    public async Task CloseButton_HidesSidebar()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await page.GetByTitle("Close").ClickAsync();

        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 3000
        });
    }

    // ── Create backup ─────────────────────────────────────────────────────

    [Fact]
    public async Task NewBackup_WithDefaultName_AppearsInList()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Open the named backup dialog.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Accept the suggested name.
        await ui.ModalConfirmButton.ClickAsync();

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // At least one backup item now shows.
        var items = page.Locator(".backup-item");
        await items.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    [Fact]
    public async Task NewBackup_WithCustomName_AppearsWithThatLabel()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var nameInput = page.Locator("#modal-backup-name");
        await nameInput.ClearAsync();
        await nameInput.FillAsync("before-refactor");

        await ui.ModalConfirmButton.ClickAsync();

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Backup item label contains the custom name.
        var backupName = page.Locator(".backup-name", new PageLocatorOptions { HasText = "before-refactor" }).First;
        await backupName.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    // ── Diff backup ───────────────────────────────────────────────────────

    [Fact]
    public async Task DiffButton_OnBackupItem_OpensModal()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Create a backup first so there is something to diff.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Click the ⊞ diff button on the first backup item.
        var diffBtn = page.Locator(".backup-item-actions").First.GetByTitle("Diff");
        await diffBtn.ClickAsync();

        // Diff modal appears with a title.
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var title = await ui.ModalTitle.InnerTextAsync();
        title.Should().Contain("Diff");

        await ui.ModalCancelButton.ClickAsync();
    }

    // ── Restore backup ────────────────────────────────────────────────────

    [Fact]
    public async Task RestoreButton_ShowsConfirmModal()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Ensure at least one backup exists.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Click restore (↩) on the first item.
        var restoreBtn = page.Locator(".backup-item-actions").First.GetByTitle("Restore");
        await restoreBtn.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var body = await ui.ModalBody.InnerTextAsync();
        body.Should().ContainAny("Restore", "restore", "backed up");

        // Cancel — don't actually restore.
        await ui.ModalCancelButton.ClickAsync();
    }

    // ── Delete backup ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBackup_AfterConfirm_RemovesItemFromList()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Production.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Create.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var countBefore = await page.Locator(".backup-item").CountAsync();

        // Delete first item.
        var deleteBtn = page.Locator(".backup-item-actions").First.GetByTitle("Delete");
        await deleteBtn.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var countAfter = await page.Locator(".backup-item").CountAsync();
        countAfter.Should().Be(countBefore - 1);
    }

    // ── Audit tab ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditTab_ShowsEntriesAfterEdit()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Make an edit first so audit has something.
        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("AuditTestKey", "\"hello\"");

        // Open sidebar → audit tab.
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.AuditTab.ClickAsync();

        await page.WaitForTimeoutAsync(600);

        // Either audit entries appear, or the "disabled" message is shown (depends on options).
        var auditContent = await ui.AuditList.InnerTextAsync();
        auditContent.Should().ContainAny(
            "AuditTestKey", "Add", "Update", "disabled", "Enable");
    }

    [Fact]
    public async Task AuditTab_RefreshButton_ReloadsEntries()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.AuditTab.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var refreshBtn = page.Locator(".rs-actions", new PageLocatorOptions { HasText = "Refresh" }).First;
        // Should not throw — a second load should complete without error.
        await refreshBtn.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var errorToastCount = await ui.ErrorToast.CountAsync();
        errorToastCount.Should().Be(0);
    }

    // ── Tab switching ─────────────────────────────────────────────────────

    [Fact]
    public async Task TabSwitch_BackupsToAudit_TogglesActiveClass()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var backupsTabClasses = await ui.BackupsTab.GetAttributeAsync("class");
        backupsTabClasses.Should().Contain("active");

        await ui.AuditTab.ClickAsync();

        var auditTabClasses = await ui.AuditTab.GetAttributeAsync("class");
        auditTabClasses.Should().Contain("active");

        backupsTabClasses = await ui.BackupsTab.GetAttributeAsync("class");
        backupsTabClasses.Should().NotContain("active");
    }
}
