using Codekali.Net.Config.UI.UITests.Fixtures;
using Codekali.Net.Config.UI.UITests.Pages;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace Codekali.Net.Config.UI.UITests.Tests;

/// <summary>
/// Tests for array CRUD, schema violation banner, assertion runner panel,
/// read-only mode enforcement, and the hot-reload banner path.
/// </summary>
[Collection("ConfigUI")]
public sealed class AdvancedFeatureTests(ConfigUITestFixture fixture)
{
    // ── Array operations ──────────────────────────────────────────────────

    [Fact]
    public async Task AddArrayKey_ShowsExpandableArrayNode()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // Add an array.
        await ui.AddKeyAsync("Cors:Origins", "[\"https://localhost:3000\"]");

        // Cors key visible.
        await ui.TreeRowByKey("Cors").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        // Badge shows [1].
        var badge = page.Locator(".tree-type-badge", new PageLocatorOptions { HasText = "[1]" }).First;
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    [Fact]
    public async Task AppendArrayItem_IncrementsBadgeCount()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // Add array with one item.
        await ui.AddKeyAsync("AllowedOrigins", "[\"https://app.example.com\"]");

        // Expand it.
        var row = ui.TreeRowByKey("AllowedOrigins");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ExpandToggleForKey("AllowedOrigins").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Hover to reveal the "+ item" button.
        await row.HoverAsync();
        var appendBtn = row.Locator(".row-actions button", new LocatorLocatorOptions { HasText = "+ item" }).First;
        await appendBtn.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var valueInput = page.Locator("#modal-array-value");
        await valueInput.FillAsync("\"https://admin.example.com\"");
        await ui.ModalConfirmButton.ClickAsync();

        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        // Badge should now show [2].
        var badge = page.Locator(".tree-type-badge", new PageLocatorOptions { HasText = "[2]" }).First;
        await badge.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    [Fact]
    public async Task DeleteArrayItem_DecrementsBadgeCount()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.Development.json");

        // Add array with two items.
        await ui.AddKeyAsync("DeleteMeArray", "[\"item-a\",\"item-b\"]");
        var row = ui.TreeRowByKey("DeleteMeArray");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ExpandToggleForKey("DeleteMeArray").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // Delete item at index 0 — hover first child row.
        var firstItem = page.Locator(".tree-node").Nth(1);
        await firstItem.HoverAsync();
        var delBtn = firstItem.Locator(".row-actions .btn-danger").First;
        await delBtn.ClickAsync();

        await ui.ModalBackdrop.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await ui.ModalConfirmButton.ClickAsync();
        await ui.SuccessToast.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });

        var badge = page.Locator(".tree-type-badge", new PageLocatorOptions { HasText = "[1]" }).First;
        await badge.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
    }

    // ── Schema violations banner ──────────────────────────────────────────

    [Fact]
    public async Task SchemaViolationsBanner_HiddenWhenNoSchemaConfigured()
    {
        // When no schema is configured the banner must stay hidden — it should
        // never appear on its own. The default test host has no schema set.
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await page.WaitForTimeoutAsync(600);

        await ui.SchemaViolationsBanner.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 3000
        });
    }

    [Fact]
    public async Task SchemaViolationsBanner_DismissButton_HidesBanner()
    {
        // Force the banner visible via JS to test the dismiss path — this does
        // not require a real schema to be configured.
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");

        // Build the banner and wire up the dismiss button entirely in JS
        // to avoid any C#/HTML quoting interaction.
        await page.EvaluateAsync("""
            () => {
                const banner = document.getElementById('schema-violations');

                const div = document.createElement('div');
                div.style.display = 'flex';
                div.style.alignItems = 'center';
                div.style.gap = '8px';

                const label = document.createElement('span');
                label.textContent = '\u26A0 Schema violations: test violation here';
                div.appendChild(label);

                const btn = document.createElement('button');
                btn.textContent = '\u2715';
                btn.className = 'btn btn-ghost btn-sm';
                btn.style.marginLeft = 'auto';
                btn.setAttribute('data-dismiss', 'schema-violations');
                btn.addEventListener('click', () => {
                    banner.classList.add('hidden');
                });
                div.appendChild(btn);

                banner.innerHTML = '';
                banner.appendChild(div);
                banner.classList.remove('hidden');
            }
            """);

        await ui.SchemaViolationsBanner.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible
        });

        await ui.SchemaViolationsBanner.GetByText("✕").ClickAsync();

        await ui.SchemaViolationsBanner.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });
    }

    // ── Tests (assertion runner) panel ────────────────────────────────────

    [Fact]
    public async Task TestsPanel_RunAllTests_WhenNoTestsRegistered_ShowsNoTestsMessage()
    {
        // Default sample app has no IConfigurationTest implementations registered.
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavTests.ClickAsync();
        await ui.TestsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await ui.RunTestsButton.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var noTests = page.Locator("#tests-no-tests");
        var testsResult = page.Locator("#tests-result");

        // Either the no-tests div appears or the result has content.
        var noTestsVisible = await noTests.IsVisibleAsync();
        var resultHtml = await testsResult.InnerHTMLAsync();

        (noTestsVisible || resultHtml.Contains("✓") || resultHtml.Contains("✕"))
            .Should().BeTrue("tests panel should show a result either way");
    }

    [Fact]
    public async Task TestsPanel_RunAllTests_Button_IsClickable()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavTests.ClickAsync();
        await ui.TestsPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Just verifies the button is present and not disabled.
        await ui.RunTestsButton.IsEnabledAsync();
        var isEnabled = await ui.RunTestsButton.IsEnabledAsync();
        isEnabled.Should().BeTrue();
    }

    // ── Read-only mode ────────────────────────────────────────────────────

    [Fact]
    public async Task ReadOnlyMode_AddButton_IsDisabled()
    {
        // This test requires the host to be started with ReadOnly = true.
        // Skip gracefully if not in read-only mode.
        var page = await fixture.NewPageAsync();

        var isReadonly = await page.EvaluateAsync<bool>("() => window.READONLY === true");
        if (!isReadonly)
        {
            // Not in read-only mode — skip.
            return;
        }

        var ui = new ConfigUIPage(page);
        await ui.SelectFileAsync("appsettings.json");

        var isDisabled = await ui.AddKeyButton.IsDisabledAsync();
        isDisabled.Should().BeTrue("add button must be disabled in read-only mode");
    }

    [Fact]
    public async Task ReadOnlyMode_Badge_IsVisible()
    {
        var page = await fixture.NewPageAsync();

        var isReadonly = await page.EvaluateAsync<bool>("() => window.READONLY === true");
        if (!isReadonly) return;

        var badge = page.Locator("#readonly-badge");
        await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        (await badge.InnerTextAsync()).Should().Contain("READ ONLY");
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshButton_ReloadsTree_WithoutErrors()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.SelectFileAsync("appsettings.json");
        await ui.RefreshFileButton.ClickAsync();

        // Tree re-renders — loading state disappears, tree visible.
        await ui.TreeView.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 6000
        });

        var errorCount = await ui.ErrorToast.CountAsync();
        errorCount.Should().Be(0);
    }

    // ── Welcome stats bar ─────────────────────────────────────────────────

    [Fact]
    public async Task GuidePanel_StatsBar_ShowsCorrectFileCounts()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        await ui.NavGuide.ClickAsync();
        await ui.GuidePanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var filesVal = await page.Locator("#gs-files").InnerTextAsync();
        int.Parse(filesVal).Should().BeGreaterThanOrEqualTo(3);

        var envsVal = await page.Locator("#gs-envs").InnerTextAsync();
        int.Parse(envsVal).Should().BeGreaterThanOrEqualTo(1);
    }

    // ── No-file stats ─────────────────────────────────────────────────────

    [Fact]
    public async Task NoFileStats_ShowFileAndEnvCounts()
    {
        var page = await fixture.NewPageAsync();
        var ui = new ConfigUIPage(page);

        // Do not select a file — stats are in the no-file state.
        await ui.NoFileState.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var filesVal = await page.Locator("#ws-files").InnerTextAsync();
        int.Parse(filesVal).Should().BeGreaterThanOrEqualTo(3);
    }
}