using Microsoft.Playwright;

namespace Codekali.Net.Config.UI.UITests.Pages;

/// <summary>
/// Page Object Model for the Config UI.
///
/// Every selector lives here. Tests use these methods exclusively —
/// never raw selectors — so a CSS class rename only breaks this one file.
/// </summary>
public sealed class ConfigUIPage(IPage page)
{
    public IPage Page { get; } = page;

    // ── Sidebar ───────────────────────────────────────────────────────────

    public ILocator FileList => Page.Locator("#file-list");
    public ILocator FileItems => Page.Locator("#file-list .file-item");
    public ILocator ActiveFileItem => Page.Locator("#file-list .file-item.active");
    public ILocator RefreshFilesButton => Page.Locator("#file-list").Locator("..").GetByTitle("Refresh list");

    public ILocator NavEditor => Page.Locator("#nav-editor");
    public ILocator NavSwap => Page.Locator("#nav-swap");
    public ILocator NavDiff => Page.Locator("#nav-diff");
    public ILocator NavTests => Page.Locator("#nav-tests");
    public ILocator NavGuide => Page.Locator("#nav-guide");

    // ── Toolbar ───────────────────────────────────────────────────────────

    public ILocator ToolbarRow1 => Page.Locator("#editor-toolbar-row1");
    public ILocator ToolbarRow2 => Page.Locator("#editor-toolbar-row2");
    public ILocator ToolbarTitle => Page.Locator("#toolbar-title");
    public ILocator SearchInput => Page.Locator("#search-input");
    public ILocator TreeToggle => Page.Locator("#toggle-tree");
    public ILocator RawToggle => Page.Locator("#toggle-raw");
    public ILocator AddKeyButton => Page.Locator("#add-btn");
    public ILocator BackupsButton => Page.Locator("#backup-btn");
    public ILocator RefreshFileButton => Page.GetByTitle("Re-fetch this file from disk");

    // ── No-file state ─────────────────────────────────────────────────────

    public ILocator NoFileState => Page.Locator("#editor-no-file");
    public ILocator NoFileTitle => Page.Locator(".no-file-title");

    // ── Editor panels ─────────────────────────────────────────────────────

    public ILocator TreeView => Page.Locator("#tree-view");
    public ILocator RawView => Page.Locator("#raw-view");
    public ILocator MonacoContainer => Page.Locator("#monaco-editor-container");
    public ILocator RawEditorFallback => Page.Locator("#raw-editor-ta");
    public ILocator SchemaViolationsBanner => Page.Locator("#schema-violations");

    // ── Add key form ──────────────────────────────────────────────────────

    public ILocator AddForm => Page.Locator("#add-form");
    public ILocator AddKeyInput => Page.Locator("#add-key");
    public ILocator AddValueInput => Page.Locator("#add-value");
    public ILocator AddSubmitButton => Page.Locator("#add-form").GetByText("Add");
    public ILocator AddCancelButton => Page.Locator("#add-form").GetByText("Cancel");

    // ── Tree rows ─────────────────────────────────────────────────────────

    public ILocator TreeRows => Page.Locator(".tree-row");
    public ILocator TreeKeys => Page.Locator(".tree-key");

    public ILocator TreeRowByKey(string key) =>
        Page.Locator(".tree-row", new PageLocatorOptions { HasText = key }).First;

    public ILocator ExpandToggleForKey(string key) =>
        TreeRowByKey(key).Locator(".tree-toggle");

    // ── Inline editor ─────────────────────────────────────────────────────

    public ILocator InlineEditor => Page.Locator(".inline-editor").First;
    public ILocator InlineInput => Page.Locator(".inline-input").First;

    // ── Modal ─────────────────────────────────────────────────────────────

    public ILocator ModalBackdrop => Page.Locator("#modal-backdrop");
    public ILocator ModalTitle => Page.Locator("#modal-title");
    public ILocator ModalBody => Page.Locator("#modal-body");
    public ILocator ModalConfirmButton => Page.Locator("#modal-confirm");
    public ILocator ModalCancelButton => Page.Locator("#modal-cancel");

    // ── Toast ─────────────────────────────────────────────────────────────

    public ILocator ToastContainer => Page.Locator("#toast-container");
    public ILocator Toasts => Page.Locator(".toast");
    public ILocator SuccessToast => Page.Locator(".toast.success");
    public ILocator ErrorToast => Page.Locator(".toast.error");

    // ── Swap panel ────────────────────────────────────────────────────────

    public ILocator SwapPanel => Page.Locator("#view-swap");
    public ILocator SwapSourceSelect => Page.Locator("#swap-source");
    public ILocator SwapTargetSelect => Page.Locator("#swap-target");
    public ILocator SwapOpSelect => Page.Locator("#swap-op");
    public ILocator SwapKeysList => Page.Locator("#swap-keys");
    public ILocator SwapOverwriteCheckbox => Page.Locator("#swap-overwrite");
    public ILocator SwapExecuteButton => Page.GetByText("Execute ⇄");

    // ── Diff panel ────────────────────────────────────────────────────────

    public ILocator DiffPanel => Page.Locator("#view-diff");
    public ILocator DiffSourceSelect => Page.Locator("#diff-source");
    public ILocator DiffTargetSelect => Page.Locator("#diff-target");
    public ILocator DiffCompareButton => Page.GetByText("Compare ⊞");
    public ILocator DiffResult => Page.Locator("#diff-result");

    // ── Right sidebar (backups + audit) ───────────────────────────────────

    public ILocator RightSidebar => Page.Locator("#right-sidebar");
    public ILocator BackupList => Page.Locator("#backup-list");
    public ILocator AuditList => Page.Locator("#audit-list");
    public ILocator NewBackupButton => Page.GetByText("+ New Backup");
    public ILocator BackupsTab => Page.Locator("#rs-tab-backups");
    public ILocator AuditTab => Page.Locator("#rs-tab-audit");

    // ── Guide panel ───────────────────────────────────────────────────────

    public ILocator GuidePanel => Page.Locator("#view-guide");

    // ── Tests panel ───────────────────────────────────────────────────────

    public ILocator TestsPanel => Page.Locator("#view-tests");
    public ILocator RunTestsButton => Page.GetByText("▶ Run All Tests");
    public ILocator TestsSummary => Page.Locator("#tests-summary");

    // ── Composite actions ─────────────────────────────────────────────────

    /// <summary>Clicks a file in the sidebar and waits for the tree to render.</summary>
    public async Task SelectFileAsync(string fileName)
    {
        await Page.Locator($"#file-list .file-item", new PageLocatorOptions { HasText = fileName })
                  .First
                  .ClickAsync();
        await TreeView.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
    }

    /// <summary>Opens the Add Key form, fills it, and submits. Waits for a success toast.</summary>
    public async Task AddKeyAsync(string keyPath, string jsonValue)
    {
        await AddKeyButton.ClickAsync();
        await AddForm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await AddKeyInput.FillAsync(keyPath);
        await AddValueInput.FillAsync(jsonValue);
        await AddSubmitButton.ClickAsync();
        await SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    /// <summary>Double-clicks a tree row to open the inline editor, sets a value, and saves.</summary>
    public async Task EditKeyAsync(string key, string newValue)
    {
        var row = TreeRowByKey(key);
        await row.DblClickAsync();
        await InlineInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await InlineInput.FillAsync(newValue);
        await InlineInput.PressAsync("Enter");
        await SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    /// <summary>Clicks the delete button on a tree row and confirms the modal.</summary>
    public async Task DeleteKeyAsync(string key)
    {
        var row = TreeRowByKey(key);
        await row.HoverAsync();
        await row.Locator(".row-actions .btn-danger").ClickAsync();
        await ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ModalConfirmButton.ClickAsync();
        await SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
    }

    /// <summary>Waits for a toast of any type to appear and returns its text.</summary>
    public async Task<string> WaitForToastAsync(int timeoutMs = 5000)
    {
        await Toasts.First.WaitForAsync(new LocatorWaitForOptions { Timeout = timeoutMs });
        return await Toasts.First.InnerTextAsync();
    }

    /// <summary>Gets the Monaco editor content via JS evaluation (bypasses the DOM).</summary>
    public async Task<string> GetMonacoContentAsync()
    {
        var content = await Page.EvaluateAsync<string>(
            "() => window._monacoInstance?.getValue() ?? ''");
        return content ?? string.Empty;
    }

    /// <summary>Sets Monaco editor content via JS and triggers the model change event.</summary>
    public async Task SetMonacoContentAsync(string json)
    {
        await Page.EvaluateAsync(
            "content => window._monacoInstance?.setValue(content)", json);
    }
}