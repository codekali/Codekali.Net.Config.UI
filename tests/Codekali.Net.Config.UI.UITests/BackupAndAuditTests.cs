using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests;

[Collection("ConfigUI")]
public sealed class BackupAndAuditTests(ConfigUITestFixture fixture)
{
    // ── Helper: open sidebar ──────────────────────────────────────────────
    private static async Task OpenSidebarAsync(IPage page, ConfigUIPage ui, string fileName)
    {
        await ui.SelectFileAsync(fileName);
        // The Audits and Backups button is in toolbar-row2 which is only visible
        // after a file is selected — wait for it before clicking.
        await ui.BackupsButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 4000
        });
    }

    // ── Sidebar open / close ──────────────────────────────────────────────

    [Fact]
    public async Task BackupsButton_NoFileSelected_ShowsWarning()
    {
        var page = await fixture.NewPageAsync();
        new ConfigUIPage(page);

        // Do NOT select a file — button is hidden (toolbar not visible).
        // Trigger the guard by calling it via JS directly.
        await page.EvaluateAsync("() => window.openRightSidebar()");

        var warnToast = page.Locator(".toast.warn");
        await warnToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 });
        (await warnToast.InnerTextAsync()).Should().ContainAny("Select", "file");
    }

    [Fact]
    public async Task BackupsButton_WithFileSelected_OpensSidebar()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await OpenSidebarAsync(page, ui, "appsettings.json");

        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });
    }

    [Fact]
    public async Task CloseButton_HidesSidebar()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await OpenSidebarAsync(page, ui, "appsettings.json");
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

        await OpenSidebarAsync(page, ui, "appsettings.json");

        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

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

        await OpenSidebarAsync(page, ui, "appsettings.Development.json");

        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var nameInput = page.Locator("#modal-backup-name");
        await nameInput.ClearAsync();
        await nameInput.FillAsync("before-refactor");

        await ui.ModalConfirmButton.ClickAsync();

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var backupName = page.Locator(".backup-name",
            new PageLocatorOptions { HasText = "before-refactor" }).First;
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

        await OpenSidebarAsync(page, ui, "appsettings.json");

        // Create a backup first.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var diffBtn = page.Locator(".backup-item-actions").First.GetByTitle("Diff");
        await diffBtn.ClickAsync();

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

        await OpenSidebarAsync(page, ui, "appsettings.json");

        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var restoreBtn = page.Locator(".backup-item-actions").First.GetByTitle("Restore");
        await restoreBtn.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var body = await ui.ModalBody.InnerTextAsync();
        body.Should().ContainAny("Restore", "restore", "backed up");

        await ui.ModalCancelButton.ClickAsync();
    }

    // ── Delete backup ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBackup_AfterConfirm_RemovesItemFromList()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await OpenSidebarAsync(page, ui, "appsettings.Production.json");

        // Create one backup.
        await ui.NewBackupButton.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();

        // Wait for the single success toast then dismiss it by waiting for it to vanish.
        await ui.SuccessToast.First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await ui.SuccessToast.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });

        var countBefore = await page.Locator(".backup-item").CountAsync();

        // Delete first item.
        var deleteBtn = page.Locator(".backup-item-actions").First.GetByTitle("Delete");
        await deleteBtn.ClickAsync();
        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();

        // Wait for the delete success toast.
        await page.Locator(".toast.success").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await page.WaitForTimeoutAsync(300);

        var countAfter = await page.Locator(".backup-item").CountAsync();
        countAfter.Should().Be(countBefore - 1);
    }

    // ── Audit tab ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditTab_ShowsEntriesAfterEdit()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Make an edit first.
        await ui.SelectFileAsync("appsettings.Development.json");
        await ui.AddKeyAsync("AuditTestKey", "\"hello\"");

        await ui.BackupsButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.BackupsButton.ClickAsync();
        await ui.RightSidebar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.AuditTab.ClickAsync();

        await page.WaitForTimeoutAsync(600);

        var auditContent = await ui.AuditList.InnerTextAsync();
        auditContent.Should().ContainAny(
            "AuditTestKey", "Add", "Update", "disabled", "Enable");
    }

    [Fact]
    public async Task AuditTab_RefreshButton_ReloadsEntries()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await OpenSidebarAsync(page, ui, "appsettings.json");
        await ui.AuditTab.ClickAsync();
        await page.WaitForTimeoutAsync(400);

        var refreshBtn = page.GetByText("↻ Refresh");
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

        await OpenSidebarAsync(page, ui, "appsettings.json");

        var backupsTabClasses = await ui.BackupsTab.GetAttributeAsync("class");
        backupsTabClasses.Should().Contain("active");

        await ui.AuditTab.ClickAsync();

        var auditTabClasses = await ui.AuditTab.GetAttributeAsync("class");
        auditTabClasses.Should().Contain("active");

        backupsTabClasses = await ui.BackupsTab.GetAttributeAsync("class");
        backupsTabClasses.Should().NotContain("active");
    }
}