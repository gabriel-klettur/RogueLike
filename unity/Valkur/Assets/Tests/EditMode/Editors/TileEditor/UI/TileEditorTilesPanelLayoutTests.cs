using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Regression-guard tests for the TILES panel's TopRow layout invariants.
    ///
    /// History — what these tests prevent
    /// ────────────────────────────────────
    /// The Tiles panel's TopRow hosts SELECTED + CATEGORIES side-by-side. Its
    /// height was originally pinned to a fixed 80 px which wasted space when
    /// CATEGORIES was short and clipped content when it was tall. We rewrote
    /// the layout to be content-driven, but ran into two layout-system traps
    /// in succession:
    ///
    ///   1. <b>Inner-flex propagation</b> — the SELECTED preview's "Name" TMP
    ///      had <c>LayoutElement.flexibleHeight = 1</c>. That flex bubbled up
    ///      through the Info VLG → SelectedPreview HLG → LEFT-column VLG, and
    ///      the LEFT VLG distributed every leftover pixel into the SELECTED
    ///      row → the yellow Img outline ended up tall and thin instead of a
    ///      40 × 40 thumbnail. <b>Fix:</b> pin SelectedPreview /
    ///      ConfigureRow / Img with <c>flexibleHeight = 0</c>.
    ///
    ///   2. <b>HLG forced-flex publishing</b> — the TopRow HLG has
    ///      <c>childForceExpandHeight = true</c>. Internally that forces every
    ///      column's flex to <c>max(child.flex, 1) = 1</c> for the layout
    ///      pass. When the HLG then publishes its OWN <c>flexibleHeight</c> to
    ///      the panel-content VLG, it returned <c>max(LEFT.flex=1, RIGHT.flex=1) = 1</c>.
    ///      The panel VLG saw <c>TopRow.flex = 1</c> AND <c>TilePicker.flex = 1</c>,
    ///      so half the surplus space went to the TopRow — leaving a fat
    ///      empty band between CATEGORIES and the TILES section, with the
    ///      picker noticeably undersized. <b>Fix:</b> explicitly set
    ///      <c>LayoutElement.flexibleHeight = 0</c> on the TopRow root —
    ///      LayoutElement priority (1) overrides the HLG's value (priority 0)
    ///      so the panel VLG reads 0 and routes all surplus to the picker.
    ///
    /// If either invariant regresses, the visual symptom is one of:
    ///   • SELECTED yellow rect stretched vertically (invariant 1)
    ///   • Empty band between CATEGORIES and TILES sections (invariant 2)
    ///
    /// These tests assert the structural fixes — not the final pixel layout —
    /// so they survive minor design tweaks (cell heights, padding) but break
    /// loudly the moment someone removes a critical <c>flexibleHeight = 0</c>.
    /// </summary>
    [TestFixture]
    public class TileEditorTilesPanelLayoutTests
    {
        private GameObject _canvasGo;

        [SetUp]
        public void SetUp()
        {
            // The static UI builder calls Button.AddListener with the null
            // callbacks the test passes for unrelated panels (tools, layers,
            // …). UGUI's UnityEvent throws IndexOutOfRangeException when the
            // listener is null; LogAssert.ignoreFailingMessages keeps those
            // unrelated exceptions from failing the layout-invariant assertion
            // we actually care about.
            LogAssert.ignoreFailingMessages = true;
            _canvasGo = new GameObject("TilesPanelLayout_Canvas", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. TopRow root — the regression that motivated this whole file
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TopRow_LayoutElement_FlexibleHeight_IsZero()
        {
            // CRITICAL regression guard. If this flips back to -1 (default,
            // "ignored"), the parent panel VLG will fall back to the TopRow
            // HLG's published flex (which is 1 because childForceExpandHeight =
            // true forces every column to flex >= 1). The panel will then split
            // surplus space between TopRow and the TilePicker, leaving an
            // empty band above the picker.
            var refs = BuildTilesPanel();
            var le = refs.TilesTopRow.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "TopRow must carry a LayoutElement so we can override its flex.");
            Assert.AreEqual(0f, le.flexibleHeight,
                "TopRow.LayoutElement.flexibleHeight MUST stay pinned at 0 — otherwise the " +
                "panel-content VLG will siphon picker surplus into the TopRow. See class docs.");
        }

        [Test]
        public void TopRow_LayoutElement_PreferredHeight_IsContentDriven()
        {
            // -1 means "ignored" — the LayoutElement defers to the HLG's own
            // preferredHeight (= max of LEFT/RIGHT column preferreds). If a
            // designer hard-codes preferredHeight back to a number, the row
            // stops shrinking with the CATEGORIES list.
            var refs = BuildTilesPanel();
            var le = refs.TilesTopRow.GetComponent<LayoutElement>();
            Assert.AreEqual(-1f, le.preferredHeight,
                "TopRow.preferredHeight must be -1 so the HLG drives the row height from " +
                "its column content (max of LEFT preview/configure column and RIGHT categories column).");
        }

        [Test]
        public void TopRow_LayoutElement_MinHeight_HasFloor()
        {
            // The minHeight floor protects against an empty-state collapse
            // (no categories yet, no SELECTED). Doesn't lock a specific
            // number — only verifies a sane non-zero floor exists.
            var refs = BuildTilesPanel();
            var le = refs.TilesTopRow.GetComponent<LayoutElement>();
            Assert.Greater(le.minHeight, 0f,
                "TopRow.minHeight must define a sane floor so the row never collapses to zero " +
                "in empty-state (no categories, no SELECTED tile).");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. TilePicker — must be the only flex consumer left
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TilePicker_LayoutElement_FlexibleHeight_IsOne()
        {
            // The picker is the only row that should publish flex > 0 to the
            // panel-content VLG. Surplus panel space then routes 100% to the
            // picker, which engages its inner ScrollRect when it grows enough.
            var refs = BuildTilesPanel();
            var le = refs.TileScrollRect.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "TilePicker must carry a LayoutElement.");
            Assert.AreEqual(1f, le.flexibleHeight,
                "TilePicker.flexibleHeight must be 1 so the panel-content VLG routes all " +
                "surplus space to the picker (instead of leaving an empty band above it).");
        }

        [Test]
        public void TilePicker_LayoutElement_MinHeight_KeepsScrollUsable()
        {
            // A min floor ensures the picker is always tall enough to show at
            // least a few tile rows even when the panel is dragged short.
            var refs = BuildTilesPanel();
            var le = refs.TileScrollRect.GetComponent<LayoutElement>();
            Assert.GreaterOrEqual(le.minHeight, 100f,
                "TilePicker.minHeight must keep a usable floor so the user can always see at " +
                "least a couple of tile rows even when the panel is dragged short.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. SELECTED preview row + Img — pinned 48 px row with 40 × 40 square
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void SelectedPreviewImg_IsConstant40x40()
        {
            // The yellow-outlined SELECTED thumbnail must be a stable square.
            // Without these explicit min/preferred + zero-flex pins it inherits
            // its parent HLG's stretched height (which scales with the TopRow,
            // which scales with CATEGORIES) and ends up tall and narrow.
            var refs = BuildTilesPanel();
            var imgGo = refs.SelectedTilePreviewImg.gameObject;
            var le = imgGo.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "SELECTED Img must carry a LayoutElement to pin its size.");
            Assert.AreEqual(40f, le.preferredWidth,  "Img preferredWidth must be 40 (square thumbnail).");
            Assert.AreEqual(40f, le.preferredHeight, "Img preferredHeight must be 40 (square thumbnail).");
            Assert.AreEqual(40f, le.minWidth,        "Img minWidth must be 40 — pin the floor.");
            Assert.AreEqual(40f, le.minHeight,       "Img minHeight must be 40 — pin the floor.");
            Assert.AreEqual(0f,  le.flexibleWidth,
                "Img flexibleWidth must be 0 so the parent HLG never stretches it horizontally.");
            Assert.AreEqual(0f,  le.flexibleHeight,
                "Img flexibleHeight must be 0 so the parent HLG never stretches it vertically.");
        }

        [Test]
        public void SelectedPreviewRow_IsPinnedAt48px_WithNoFlex()
        {
            // The row that hosts the SELECTED Img + name must NOT absorb
            // leftover space from the LEFT column — otherwise the Name TMP's
            // own flex propagates upward and the row stretches.
            var refs = BuildTilesPanel();
            var rowGo = refs.SelectedTilePreviewImg.transform.parent.gameObject;
            var le = rowGo.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "SELECTED row must carry a LayoutElement.");
            Assert.AreEqual(48f, le.preferredHeight,
                "SELECTED row preferredHeight must be 48 (room for the 40 × 40 thumbnail + 4 px padding top/bottom).");
            Assert.AreEqual(48f, le.minHeight,
                "SELECTED row minHeight must equal preferredHeight — pin both edges to lock the row.");
            Assert.AreEqual(0f,  le.flexibleHeight,
                "SELECTED row flexibleHeight must be 0 to stop inner flex (Name TMP) from " +
                "propagating up and stretching the row vertically.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. CONFIGURE row — pinned at 26 px, button auto-sizes to label
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ConfigureRow_IsPinnedAt26px_WithNoFlex()
        {
            // Same propagation guard as SELECTED row. The row hosts one
            // auto-sized button; without flex=0 the row would absorb LEFT
            // column surplus and the button would float into vertical space
            // it doesn't need.
            var refs = BuildTilesPanel();
            var rowGo = refs.ConfigureTilesetBtn.transform.parent.gameObject;
            var le = rowGo.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "CONFIGURE row must carry a LayoutElement.");
            Assert.AreEqual(26f, le.preferredHeight,
                "CONFIGURE row preferredHeight must be 26 (room for the button at 22 px + padding).");
            Assert.AreEqual(26f, le.minHeight,
                "CONFIGURE row minHeight must equal preferredHeight — pin both edges to lock the row.");
            Assert.AreEqual(0f,  le.flexibleHeight,
                "CONFIGURE row flexibleHeight must be 0 — same propagation guard as SELECTED row.");
        }

        [Test]
        public void ConfigureBtn_AutoSizesWidthToLabel()
        {
            // The button width must follow the label text width — covers
            // "CONFIGURE TILESET", "NO RULESET FOR CATEGORY",
            // "PICK A CATEGORY FIRST" and "CONFIGURE: <category>" without
            // wrapping, ellipsis or fixed-width chrome.
            var refs = BuildTilesPanel();
            var btnGo = refs.ConfigureTilesetBtn.gameObject;

            var csf = btnGo.GetComponent<ContentSizeFitter>();
            Assert.IsNotNull(csf,
                "CONFIGURE button must carry a ContentSizeFitter so its RectTransform follows the label width.");
            Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, csf.horizontalFit,
                "CSF.horizontalFit must be PreferredSize so the button matches the label's preferredWidth.");
            Assert.AreEqual(ContentSizeFitter.FitMode.Unconstrained, csf.verticalFit,
                "CSF.verticalFit must be Unconstrained so the parent HLG (not CSF) drives the button height.");

            var btnHlg = btnGo.GetComponent<HorizontalLayoutGroup>();
            Assert.IsNotNull(btnHlg,
                "CONFIGURE button must carry an HLG to read the TMP label's preferredWidth and feed it to CSF.");
            Assert.IsTrue(btnHlg.childControlWidth,
                "Button HLG.childControlWidth must be true so it reads the TMP's ILayoutElement.preferredWidth.");
            Assert.IsFalse(btnHlg.childForceExpandWidth,
                "Button HLG.childForceExpandWidth must be false — otherwise CSF + force-expand fight each other and the button width oscillates.");
        }

        [Test]
        public void ConfigureBtnLabel_DoesNotWrap()
        {
            // Word-wrap inside the auto-sized button would let TMP report a
            // shrunken preferredWidth → CSF would shrink the button → less
            // horizontal room for the label → TMP wraps tighter → infinite
            // shrink loop. Single-line labels are mandatory.
            var refs = BuildTilesPanel();
            Assert.IsNotNull(refs.ConfigureTilesetBtnLabel, "CONFIGURE label must be wired.");
            Assert.IsFalse(refs.ConfigureTilesetBtnLabel.enableWordWrapping,
                "CONFIGURE label must have wordWrap disabled so its preferredWidth is stable for the auto-sizing CSF.");
        }

        [Test]
        public void ConfigureRow_LeftAlignsAutoSizedButton()
        {
            // The auto-sized button must sit on the LEFT edge of the row
            // (not centred or stretched), matching every other "small button
            // in a wide row" pattern in the editor chrome.
            var refs = BuildTilesPanel();
            var rowGo = refs.ConfigureTilesetBtn.transform.parent.gameObject;
            var hlg = rowGo.GetComponent<HorizontalLayoutGroup>();

            Assert.IsNotNull(hlg, "CONFIGURE row must carry an HLG.");
            Assert.IsFalse(hlg.childForceExpandWidth,
                "CONFIGURE row HLG.childForceExpandWidth must be false so the button stays at its CSF-driven width " +
                "instead of being stretched across the column.");
            Assert.AreEqual(TextAnchor.MiddleLeft, hlg.childAlignment,
                "CONFIGURE row HLG.childAlignment must be MiddleLeft so the auto-sized button hugs the left edge.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. CATEGORIES scroll — content-driven height with cap
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void CategoryScroll_LayoutElement_FlexibleHeight_IsZero()
        {
            // Same flex-pinning guard as TopRow. If the scroll publishes flex,
            // it absorbs RIGHT-column surplus and that propagates up.
            var refs = BuildTilesPanel();
            var scrollGo = refs.CategoryTabsContent.parent.parent.gameObject;
            var le = scrollGo.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "CATEGORIES scroll must carry a LayoutElement.");
            Assert.AreEqual(0f, le.flexibleHeight,
                "CATEGORIES scroll.flexibleHeight must be 0 so its height is content-driven only " +
                "(via LayoutElementFollowsChildHeight), never absorbing surplus.");
        }

        [Test]
        public void CategoryScroll_HasContentDrivenHeightHelper()
        {
            // The helper mirrors the inner grid's measured height onto the
            // scroll's LayoutElement.preferredHeight. Without it the scroll
            // would render at minHeight only and the user would always have to
            // scroll even when 3 categories fit comfortably.
            var refs = BuildTilesPanel();
            var scrollGo = refs.CategoryTabsContent.parent.parent.gameObject;
            var follow = scrollGo.GetComponent<LayoutElementFollowsChildHeight>();

            Assert.IsNotNull(follow,
                "CATEGORIES scroll must carry a LayoutElementFollowsChildHeight helper so it sizes to grid content.");
            Assert.AreSame(refs.CategoryTabsContent, follow.SourceContent,
                "Helper.SourceContent must point at the categories grid content RectTransform.");
            Assert.Greater(follow.MaxHeight, 0f,
                "Helper.MaxHeight cap must be > 0 so the scroll engages instead of pushing the picker off-screen " +
                "when the project has many categories.");
            Assert.LessOrEqual(follow.MaxHeight, 200f,
                "Helper.MaxHeight cap must stay modest (<=200 px) — beyond that the TopRow eats too much of the panel.");
            Assert.Greater(follow.MinHeight, 0f,
                "Helper.MinHeight floor must be > 0 so the scrollbar handle stays interactable when the list is empty.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. Helpers
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the entire Tile Editor UI tree on the test canvas via the
        /// production static builder. Returns the resulting refs bag so each
        /// test can navigate to the specific sub-component it asserts on.
        ///
        /// Passes no-op lambdas for every callback rather than <c>null</c>:
        /// UGUI's <see cref="UnityEngine.Events.UnityEvent.AddListener"/>
        /// throws <see cref="System.IndexOutOfRangeException"/> when handed a
        /// <c>null</c> callback, and that exception bubbles through the entire
        /// builder before <see cref="LogAssert.ignoreFailingMessages"/> can
        /// suppress it — failing every test in this fixture in isolation.
        /// The no-op lambdas dodge the throw entirely; layout invariants are
        /// what we care about, not whether the click handlers fire.
        /// </summary>
        private TileEditorUIBuilder.UIRefs BuildTilesPanel()
        {
            System.Action noop = () => { };
            return TileEditorUIBuilder.BuildAll(_canvasGo.transform, new TileEditorState(),
                onToolChanged:           _ => { },
                onLayerChanged:          _ => { },
                onBrushSizeChanged:      _ => { },
                onDropdownToggle:        _ => { },
                onUndo:                  noop,
                onRedo:                  noop,
                onShowColliders:         noop,
                onDrawColliders:         noop,
                onEraseColliders:        noop,
                onPerfToggle:            noop,
                onAllPanelsToggle:       noop,
                onShowGridLines:         noop,
                onShowZoneGrid:          noop,
                onSelectModeChanged:     _ => { },
                onCopyClicked:           noop,
                onCutClicked:            noop,
                onPasteClicked:          noop,
                onClearSelectionClicked: noop,
                onMoveToLayerClicked:    _ => { });
        }
    }
}
