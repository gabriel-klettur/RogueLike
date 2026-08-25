using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Picker
{
    /// <summary>
    /// EditMode tests for the responsive layout of the F8 Tile Editor's TILES
    /// panel. Two cross-cutting concerns:
    ///
    ///   1. <c>ApplyTilesPanelResizePolicy</c> — must mark the TILES picker
    ///      AND the top row (where CATEGORIES lives) as
    ///      <c>flexibleWidth = 1</c>, while pinning every other chrome row at
    ///      <c>flexibleWidth = 0</c>. Without this, dragging the resize handle
    ///      either grows the wrong row or stretches the chrome incorrectly.
    ///
    ///   2. <c>BuildCategoryScroll</c> — must attach a <see cref="GridAutoSize"/>
    ///      sibling to the category-list content with the project's tuned
    ///      params (min 110 / max 200 / spacing 3 / height-override 22).
    ///      That component is what reflows the categories into 1/2/3+ columns
    ///      as the panel widens.
    ///
    /// Both invariants are enforced via reflection: the production code paths
    /// are static partials in <see cref="TileEditorUIBuilder"/>. Spinning up
    /// the full Tile Editor canvas just to inspect a sibling component would
    /// be heavy + brittle, so each test builds the minimum hierarchy needed.
    /// </summary>
    [TestFixture]
    public class TilesPanelResponsiveTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        // ── Reflection helpers ──────────────────────────────────────────────

        private static MethodInfo GetStatic(string name)
        {
            var m = typeof(TileEditorUIBuilder).GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(m,
                $"Reflection failed: static '{name}' not found on TileEditorUIBuilder.");
            return m;
        }

        // ── Layout-policy invariants ────────────────────────────────────────

        /// <summary>
        /// Builds the minimum hierarchy the policy walks: a content GO with a
        /// VLG and N child rows, each carrying a <see cref="LayoutElement"/>.
        /// Two of the children are pre-tagged as the picker and the top row
        /// via <see cref="TileEditorUIBuilder.UIRefs"/> so the policy can
        /// identify them.
        /// </summary>
        private (GameObject content, GameObject topRow, GameObject picker,
                 GameObject sep, GameObject footer, TileEditorUIBuilder.UIRefs refs)
            BuildPolicyFixture()
        {
            var content = new GameObject("TilesContent", typeof(RectTransform));
            _scene.Add(content);
            content.AddComponent<VerticalLayoutGroup>();

            GameObject Child(string name, bool withScrollRect = false)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(content.transform);
                go.AddComponent<LayoutElement>();
                if (withScrollRect) go.AddComponent<ScrollRect>();
                _scene.Add(go);
                return go;
            }

            var topRow = Child("TopRow");
            var sep    = Child("Separator");
            var picker = Child("TilePicker", withScrollRect: true);
            var footer = Child("Footer");

            var refs = new TileEditorUIBuilder.UIRefs
            {
                TilesTopRow    = topRow,
                TileScrollRect = picker.GetComponent<ScrollRect>(),
            };

            return (content, topRow, picker, sep, footer, refs);
        }

        private static void InvokeApplyPolicy(GameObject content, TileEditorUIBuilder.UIRefs refs)
        {
            // Signature: private static void ApplyTilesPanelResizePolicy(Transform, UIRefs).
            GetStatic("ApplyTilesPanelResizePolicy")
                .Invoke(null, new object[] { content.transform, refs });
        }

        [Test]
        public void ResizePolicy_TopRow_GetsFlexibleWidth1()
        {
            var (content, topRow, _, _, _, refs) = BuildPolicyFixture();
            InvokeApplyPolicy(content, refs);

            Assert.AreEqual(1f, topRow.GetComponent<LayoutElement>().flexibleWidth,
                "TopRow must be flexible-width so the right column (CATEGORIES) " +
                "absorbs panel resize → GridAutoSize reflows into more columns.");
        }

        [Test]
        public void ResizePolicy_Picker_GetsFlexibleWidth1()
        {
            var (content, _, picker, _, _, refs) = BuildPolicyFixture();
            InvokeApplyPolicy(content, refs);

            Assert.AreEqual(1f, picker.GetComponent<LayoutElement>().flexibleWidth,
                "Picker must be flexible-width so the TILES grid grows with the panel.");
        }

        [Test]
        public void ResizePolicy_OtherChromeRows_StayFixedOnBothAxes()
        {
            var (content, _, _, sep, footer, refs) = BuildPolicyFixture();
            InvokeApplyPolicy(content, refs);

            foreach (var fixed_ in new[] { sep, footer })
            {
                var le = fixed_.GetComponent<LayoutElement>();
                Assert.AreEqual(0f, le.flexibleWidth,
                    $"{fixed_.name}: chrome rows other than TopRow/Picker must NOT flex horizontally.");
                Assert.AreEqual(0f, le.flexibleHeight,
                    $"{fixed_.name}: chrome rows must NOT flex vertically either.");
            }
        }

        [Test]
        public void ResizePolicy_DisablesChildForceExpandWidth_OnContentVlg()
        {
            // The shared EditorUIHelpers.MakeDropPanel sets childForceExpandWidth=true
            // (so most editors get full-width rows). For the Tiles panel we
            // explicitly OVERRIDE this so per-row preferredWidth + flexibleWidth
            // actually decide each row's width — that's the only way the
            // "only TopRow + Picker grow" rule holds.
            var (content, _, _, _, _, refs) = BuildPolicyFixture();
            content.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;

            InvokeApplyPolicy(content, refs);

            Assert.IsFalse(content.GetComponent<VerticalLayoutGroup>().childForceExpandWidth,
                "Policy must locally override childForceExpandWidth so per-row " +
                "preferredWidth values are honoured.");
        }

        [Test]
        public void ResizePolicy_AllRowsHaveSameInitialPreferredWidth()
        {
            // Every chrome row gets the same preferredWidth (TILES_PANEL_ROW_W).
            // That's the "anchor" the resize delta is measured against — without
            // it the first layout pass would assign each row whatever width its
            // intrinsic ILayoutElement components compute (TMP labels, Images
            // with default 100×100, etc.), making the panel look ragged.
            var (content, topRow, picker, sep, footer, refs) = BuildPolicyFixture();
            InvokeApplyPolicy(content, refs);

            float expected = topRow.GetComponent<LayoutElement>().preferredWidth;
            Assert.Greater(expected, 0f, "Sanity: policy must set a positive preferredWidth.");

            foreach (var go in new[] { topRow, picker, sep, footer })
                Assert.AreEqual(expected, go.GetComponent<LayoutElement>().preferredWidth,
                    $"{go.name}: preferredWidth must match the panel's row width constant.");
        }

        [Test]
        public void ResizePolicy_IsIdempotent()
        {
            // Running the policy twice must produce the same end state as
            // running it once. Catches anyone who decides to e.g. compound
            // flex values or mutate preferredWidth in a non-deterministic way.
            var (content, topRow, picker, sep, footer, refs) = BuildPolicyFixture();
            InvokeApplyPolicy(content, refs);

            float topW = topRow.GetComponent<LayoutElement>().preferredWidth;
            float picW = picker.GetComponent<LayoutElement>().preferredWidth;
            float topFlex = topRow.GetComponent<LayoutElement>().flexibleWidth;
            float sepFlex = sep.GetComponent<LayoutElement>().flexibleWidth;

            InvokeApplyPolicy(content, refs);  // second pass

            Assert.AreEqual(topW, topRow.GetComponent<LayoutElement>().preferredWidth);
            Assert.AreEqual(picW, picker.GetComponent<LayoutElement>().preferredWidth);
            Assert.AreEqual(topFlex, topRow.GetComponent<LayoutElement>().flexibleWidth);
            Assert.AreEqual(sepFlex, sep.GetComponent<LayoutElement>().flexibleWidth);
            Assert.AreEqual(0f, footer.GetComponent<LayoutElement>().flexibleWidth);
        }

        // ── CategoryScroll wiring (BuildCategoryScroll) ─────────────────────

        private (GameObject scrollGo, GridAutoSize autoSize, GridLayoutGroup grid)
            BuildCategoryScrollFixture()
        {
            // Build INSIDE an inactive parent so the internal Scrollbar's
            // OnEnable doesn't fire — Unity 2022.3 has a known UGUI bug where
            // Selectable.s_Selectables can overflow under heavy EditMode test
            // suites and throw IndexOutOfRangeException intermittently. These
            // tests only inspect configured fields on GridAutoSize / GridLayout
            // / ScrollRect, not runtime behaviour, so an inactive hierarchy is
            // sufficient and rock-solid.
            var parent = new GameObject("Parent", typeof(RectTransform));
            parent.SetActive(false);
            _scene.Add(parent);
            parent.AddComponent<VerticalLayoutGroup>();

            var refs = new TileEditorUIBuilder.UIRefs();
            var args = new object[] { (Transform)parent.transform, refs };

            // BuildCategoryScroll is private static; reflection bypasses that.
            GetStatic("BuildCategoryScroll").Invoke(null, args);

            // refs was passed by ref → args[1] holds the mutated copy.
            refs = (TileEditorUIBuilder.UIRefs)args[1];

            var content = refs.CategoryTabsContent;
            Assert.IsNotNull(content,
                "BuildCategoryScroll must populate refs.CategoryTabsContent.");
            var grid = content.GetComponent<GridLayoutGroup>();
            var autoSize = content.GetComponent<GridAutoSize>();
            Assert.IsNotNull(grid,     "Content must carry a GridLayoutGroup.");
            Assert.IsNotNull(autoSize, "Content must carry a GridAutoSize sibling.");

            // scrollGo is the parent of content.viewport
            var scrollGo = content.parent.parent.gameObject;
            return (scrollGo, autoSize, grid);
        }

        [Test]
        public void CategoryScroll_HasGridAutoSize_WithRectangularCellHeight()
        {
            var (_, autoSize, _) = BuildCategoryScrollFixture();

            Assert.Greater(autoSize.CellHeightOverride, 0f,
                "Category cells must be rectangular (height override > 0) — square " +
                "cells would inflate each row to ~110-200 px tall.");
            Assert.AreEqual(22f, autoSize.CellHeightOverride, 0.01f,
                "Category row height is pinned at 22 px (TILES_CAT_BTN_H constant); " +
                "changing this without coordinating with AddCategoryTab font sizing " +
                "would clip text. If you change one, change both.");
        }

        [Test]
        public void CategoryScroll_GridAutoSize_HasResponsiveWidthRange()
        {
            var (_, autoSize, _) = BuildCategoryScrollFixture();

            Assert.Greater(autoSize.MinCellSize, 0f);
            Assert.Greater(autoSize.MaxCellSize, autoSize.MinCellSize,
                "MaxCellSize must exceed MinCellSize for the responsive range to be meaningful.");
            Assert.LessOrEqual(autoSize.MinCellSize, 120f,
                "MinCellSize must stay small enough that a single column fits in the " +
                "initial right-column width (~160 px minus scrollbar + padding).");
            Assert.GreaterOrEqual(autoSize.MaxCellSize, 150f,
                "MaxCellSize must be large enough to let one column dominate at " +
                "narrow widths without prematurely splitting to two cramped columns.");
        }

        [Test]
        public void CategoryScroll_GridLayout_UsesFixedColumnCountConstraint()
        {
            var (_, _, grid) = BuildCategoryScrollFixture();

            Assert.AreEqual(GridLayoutGroup.Constraint.FixedColumnCount, grid.constraint,
                "GridAutoSize drives constraintCount on the column axis — the " +
                "underlying GridLayoutGroup MUST be in FixedColumnCount mode.");
        }

        [Test]
        public void CategoryScroll_HasScrollRect_VerticalOnly()
        {
            // The category list grows downward as more categories register.
            // Horizontal scroll would be wrong (each row is full-width by design)
            // and disrupts the multi-column reflow.
            var (scrollGo, _, _) = BuildCategoryScrollFixture();
            var sr = scrollGo.GetComponent<ScrollRect>();
            Assert.IsNotNull(sr, "CategoryScroll must expose a ScrollRect.");
            Assert.IsFalse(sr.horizontal, "Category scroll must not scroll horizontally.");
            Assert.IsTrue (sr.vertical,   "Category scroll must scroll vertically — that's how the user reaches off-screen categories.");
        }

        [Test]
        public void CategoryScroll_HasPermanentVerticalScrollbar()
        {
            var (scrollGo, _, _) = BuildCategoryScrollFixture();
            var sr = scrollGo.GetComponent<ScrollRect>();

            Assert.AreEqual(ScrollRect.ScrollbarVisibility.Permanent,
                            sr.verticalScrollbarVisibility,
                "Permanent visibility keeps the scroll affordance discoverable even " +
                "before the user adds enough categories to need scrolling.");
        }
    }
}
