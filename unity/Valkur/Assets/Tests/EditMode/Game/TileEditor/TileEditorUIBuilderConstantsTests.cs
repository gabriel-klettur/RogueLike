using NUnit.Framework;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Tests.EditMode.TileEditor
{
    /// <summary>
    /// Layout sanity tests for the menu-bar button widths and dropdown panel sizes.
    /// These guard against accidental drift that would break the docked panel layout
    /// (e.g. UX panel overlapping PERF or going off-screen).
    /// </summary>
    public class TileEditorUIBuilderConstantsTests
    {
        // ── Button widths sanity ────────────────────────────────────────────

        [Test]
        public void MenuButtonWidths_ArePositive()
        {
            Assert.Greater(TOOLS_BTN_W,     0f);
            Assert.Greater(TILES_BTN_W,     0f);
            Assert.Greater(LAYERS_BTN_W,    0f);
            Assert.Greater(INSPECTOR_BTN_W, 0f);
            Assert.Greater(COLLIDERS_BTN_W, 0f);
            Assert.Greater(SIZE_BTN_W,      0f);
            Assert.Greater(UX_BTN_W,        0f);
            Assert.Greater(PERF_BTN_W,      0f);
        }

        [Test]
        public void UxButtonWidth_Is50()
        {
            // UX panel docking math depends on this exact value (UxX = 8 + 60 + 8 + 50 + 8 = 134).
            Assert.AreEqual(50f, UX_BTN_W);
        }

        [Test]
        public void PerfButtonWidth_Is60()
        {
            Assert.AreEqual(60f, PERF_BTN_W);
        }

        // ── Dropdown panel sizes sanity ─────────────────────────────────────

        [Test]
        public void DropdownPanels_HaveReasonableSize()
        {
            // Each dropdown should be wide enough to show its content
            // and short enough to fit on a 1600x800 reference resolution.
            Assert.That(TOOLS_DROP_W,     Is.InRange(40f,  400f));
            Assert.That(TILES_DROP_W,     Is.InRange(150f, 600f));
            Assert.That(LAYERS_DROP_W,    Is.InRange(150f, 600f));
            Assert.That(INSPECTOR_DROP_W, Is.InRange(150f, 600f));
            Assert.That(COLLIDERS_DROP_W, Is.InRange(150f, 600f));
            Assert.That(SIZE_DROP_W,      Is.InRange(150f, 600f));
            Assert.That(UX_DROP_W,        Is.InRange(200f, 600f));

            Assert.That(TOOLS_DROP_H,     Is.InRange(100f, 800f));
            Assert.That(TILES_DROP_H,     Is.InRange(100f, 800f));
            Assert.That(LAYERS_DROP_H,    Is.InRange(100f, 800f));
            Assert.That(INSPECTOR_DROP_H, Is.InRange(100f, 800f));
            Assert.That(COLLIDERS_DROP_H, Is.InRange(100f, 800f));
            Assert.That(SIZE_DROP_H,      Is.InRange(100f, 800f));
            Assert.That(UX_DROP_H,        Is.InRange(100f, 800f));
        }

        [Test]
        public void UxDropdown_IncludesHeaderInTotalHeight()
        {
            // UX panel content area = UX_DROP_H - PANEL_HDR_H must be > 0.
            Assert.Greater(UX_DROP_H - PANEL_HDR_H, 100f,
                "UX content area must be tall enough to fit several color editors");
        }

        // ── Layout / spacing ────────────────────────────────────────────────

        [Test]
        public void MenuBarHeight_FitsWithinTopOffset()
        {
            // Panels dock just below the menu bar — the offset must clear the bar.
            Assert.GreaterOrEqual(PANEL_TOP_OFFSET, MENUBAR_HEIGHT,
                "Panels would overlap the menu bar");
        }

        [Test]
        public void PanelGap_IsPositive()
        {
            Assert.Greater(PANEL_GAP, 0f);
        }

        [Test]
        public void PanelHeader_HasMinimumHeight()
        {
            // Need room for 11px title + vertical padding.
            Assert.GreaterOrEqual(PANEL_HDR_H, 18f);
        }

        // ── UX panel docking math ───────────────────────────────────────────

        [Test]
        public void UxPanelDockingX_LeavesRoomForPerfButton()
        {
            // UX panel anchors top-right with x = PANEL_GAP + PERF_BTN_W + PANEL_GAP + UX_BTN_W + PANEL_GAP.
            // It must be > PERF_BTN_W (so the panel doesn't overlap the PERF button).
            float uxX = PANEL_GAP + PERF_BTN_W + PANEL_GAP + UX_BTN_W + PANEL_GAP;
            Assert.Greater(uxX, PERF_BTN_W,
                "UX panel must dock left of (i.e. further-from-edge than) the PERF button");
        }
    }
}
