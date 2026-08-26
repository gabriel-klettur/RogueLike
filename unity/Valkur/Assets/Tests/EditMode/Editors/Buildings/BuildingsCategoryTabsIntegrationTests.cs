using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Buildings;
using Valkur.UIKit;
using Cat = Valkur.Gameplay.Buildings.BuildingCategory.Category;

namespace Valkur.Tests.EditMode.Editors.Buildings
{
    /// <summary>
    /// Wiring tests for the Buildings Editor (F10) category tabs.
    ///
    /// <see cref="BuildingCategoryTests"/> proves the classification is right;
    /// these prove the picker actually honours it — that the strip is built with the tabs
    /// the taxonomy declares, and that the category gate composes with the search box
    /// instead of replacing it.
    /// </summary>
    [TestFixture]
    public class BuildingsCategoryTabsIntegrationTests
    {
        private GameObject _canvasGo;
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            // Runtime UI construction logs TMP font warnings in EditMode; they are noise here.
            LogAssert.ignoreFailingMessages = true;

            _canvasGo = new GameObject("TestCanvas");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _rootGo = new GameObject("Root", typeof(RectTransform));
            _rootGo.transform.SetParent(_canvasGo.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null) UnityEngine.Object.DestroyImmediate(_rootGo);
            if (_canvasGo != null) UnityEngine.Object.DestroyImmediate(_canvasGo);
            LogAssert.ignoreFailingMessages = false;
        }

        private BuildingsEditorUIBuilder.UIRefs BuildUI(Action<string> onCategoryChanged = null)
        {
            return BuildingsEditorUIBuilder.BuildAll(
                _rootGo.transform,
                onDropdownToggle: _ => { },
                onUndo: () => { }, onRedo: () => { },
                onSave: () => { }, onReload: () => { },
                onModeSelect: () => { }, onModePlace: () => { },
                onModeResize: () => { }, onModeDelete: () => { },
                onAddBuilding: () => { }, onRemoveBuilding: () => { }, onAddOnSystem: () => { },
                onToggleTutorial: () => { },
                onSearchChanged: _ => { },
                onSplitChanged: _ => { },
                onZBottomMinus: () => { }, onZBottomPlus: () => { },
                onZTopMinus: () => { }, onZTopPlus: () => { },
                onGridColsMinus: () => { }, onGridColsPlus: () => { },
                onGridRowsMinus: () => { }, onGridRowsPlus: () => { },
                onColliderScope: () => { },
                onPaintSolid: () => { }, onPaintWalk: () => { }, onSaveCU: () => { },
                onDeleteBuilding: () => { },
                onResetBuilding: () => { },
                onToggleCollidersVisible: () => { },
                onCollScopeToggle: () => { },
                onBrushPaint: () => { },
                onBrushErase: () => { },
                onCollBrushSizeChanged: _ => { },
                onCollBrushSizeStepDown: () => { },
                onCollBrushSizeStepUp: () => { },
                onToggleBuildingsVisible: () => { },
                onCategoryChanged: onCategoryChanged);
        }

        // ── Strip construction ────────────────────────────────────────────────────

        [Test]
        public void BuildAll_CreatesACategoryStrip_WithAllPlusEveryCategory()
        {
            var ui = BuildUI();

            Assert.IsTrue(ui.CategoryTabStrip != null, "CategoryTabStrip must be populated.");
            Assert.AreEqual(BuildingCategory.TabOrder.Length + 1, ui.CategoryTabStrip.Count,
                "The strip is 'All' plus one tab per category.");
        }

        [Test]
        public void BuildAll_StartsOnTheAllTab()
        {
            var ui = BuildUI();

            Assert.AreEqual(0, ui.CategoryTabStrip.ActiveIndex,
                "The picker must open unfiltered, exactly as it did before the tabs existed.");
        }

        [Test]
        public void BuildAll_TabKeys_RoundTripThroughTheCategoryEnum()
        {
            var ui = BuildUI();

            // Every non-All key must parse back to a Category, because that parse is what
            // BuildingsRuntimeEditor.ActiveCategory does at runtime.
            foreach (Cat c in BuildingCategory.TabOrder)
            {
                Assert.IsTrue(ui.CategoryTabStrip.SetActive(c.ToString()),
                    $"No tab registered under key '{c}'.");
                Assert.IsTrue(Enum.TryParse(ui.CategoryTabStrip.ActiveKey, out Cat parsed),
                    $"Tab key '{ui.CategoryTabStrip.ActiveKey}' does not parse back to a Category.");
                Assert.AreEqual(c, parsed);
            }
        }

        [Test]
        public void SelectingATab_ReportsItsKeyToTheEditor()
        {
            string received = null;
            var ui = BuildUI(k => received = k);

            ui.CategoryTabStrip.SetActive(Cat.Lights.ToString());

            Assert.AreEqual(Cat.Lights.ToString(), received,
                "The tab callback is what sets _categoryFilter; without it the grid never filters.");
        }

        // ── Geometry ──────────────────────────────────────────────────────────────
        // The strip lives in a 256 px panel. The failure this guards against is silent:
        // a strip that collapses to zero height, or tabs squeezed so narrow the labels
        // wrap mid-word, both still "work" and both make the picker unusable.

        /// <summary>
        /// The Buildings panel ships hidden and only lays out once it is shown. The rebuild
        /// has to be driven from the transform that owns the VerticalLayoutGroup — the
        /// panel's Content — because the dropdown root above it has no layout group and
        /// rebuilding there leaves every child at its default 100x100.
        /// </summary>
        private static void ShowPanelAndLayout(BuildingsEditorUIBuilder.UIRefs ui)
        {
            ui.BuildingsDropdown.SetActive(true);
            var content = (RectTransform)ui.CategoryTabStrip.transform.parent;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        /// <summary>
        /// Tabs per row, read off the builder so this test cannot drift from it.
        /// </summary>
        private static int TabsPerRow()
        {
            var field = typeof(BuildingsEditorUIBuilder).GetField(
                "CATEGORY_TAB_COLUMNS", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null,
                "BuildingsEditorUIBuilder.CATEGORY_TAB_COLUMNS was renamed — update this test.");
            return (int)field.GetRawConstantValue();
        }

        [Test]
        public void CategoryStrip_LaysOutFullRowsOfReadableTabs()
        {
            var ui = BuildUI();
            var strip = ui.CategoryTabStrip;
            var stripRt = strip.GetComponent<RectTransform>();

            ShowPanelAndLayout(ui);

            // Derived, not pinned: the taxonomy grows (8 categories became 15 when the
            // second prop wave landed), and a hard-coded row count only ever records how
            // many tabs there were the day it was written. What must stay true is that the
            // strip reserves exactly the height its own rows need — the failure this
            // guards is a strip that lays out more rows than it reserved space for, which
            // overlaps the picker grid underneath it.
            int tabCount = 1 + BuildingCategory.TabOrder.Length;   // + the "All" tab
            int expectedRows = Mathf.CeilToInt(tabCount / (float)TabsPerRow());

            Assert.AreEqual(expectedRows, strip.transform.childCount,
                $"{tabCount} tabs at {TabsPerRow()} per row must occupy {expectedRows} rows.");

            var le = strip.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.AreEqual(expectedRows * 22f + (expectedRows - 1) * 2f, le.preferredHeight,
                "Reserved height must cover every row plus the inter-row spacing.");

            Assert.Greater(stripRt.rect.width, 300f,
                "The strip must span the panel content (368 px); a collapsed width means the " +
                "parent layout ignored it.");

            foreach (Transform row in strip.transform)
            {
                Assert.AreEqual(stripRt.rect.width, ((RectTransform)row).rect.width, 1f,
                    $"Row '{row.name}' must span the strip.");
                foreach (Transform tab in row)
                {
                    var rt = (RectTransform)tab;
                    Assert.Greater(rt.rect.width, 80f,
                        $"Tab '{tab.name}' is only {rt.rect.width:0} px wide — its label will wrap.");
                    Assert.GreaterOrEqual(rt.rect.height, 20f,
                        $"Tab '{tab.name}' is only {rt.rect.height:0} px tall.");
                }
            }
        }

        /// <summary>Walks up from <paramref name="node"/> to the child that sits directly under
        /// <paramref name="panel"/>, so widgets nested at different depths can be ordered.</summary>
        private static Transform DirectChildOf(Transform panel, Transform node)
        {
            while (node != null && node.parent != panel) node = node.parent;
            return node;
        }

        [Test]
        public void CategoryStrip_SitsBetweenTheSearchBoxAndTheGrid()
        {
            var ui = BuildUI();
            Transform panel = ui.CategoryTabStrip.transform.parent;
            Assert.IsNotNull(panel, "The strip must be parented into the Buildings panel.");

            // includeInactive: the Buildings panel ships hidden, and the default overload
            // skips inactive ancestors and silently answers null.
            var scroll = ui.PickerContent.GetComponentInParent<UnityEngine.UI.ScrollRect>(true);
            Assert.IsNotNull(scroll, "The picker grid must live inside a ScrollRect.");

            Transform search = DirectChildOf(panel, ui.SearchBox.transform);
            Transform grid = DirectChildOf(panel, scroll.transform);
            Assert.IsNotNull(search, "Search box is not inside the Buildings panel.");
            Assert.IsNotNull(grid, "Picker grid is not inside the Buildings panel.");

            Assert.Less(search.GetSiblingIndex(), ui.CategoryTabStrip.transform.GetSiblingIndex(),
                "Search stays on top.");
            Assert.Less(ui.CategoryTabStrip.transform.GetSiblingIndex(), grid.GetSiblingIndex(),
                "Tabs must sit above the grid they filter.");
        }

        // ── Filter composition (the gate RefreshPicker applies per template) ───────

        private static BuildingsRuntimeEditor MakeEditor(string categoryFilter)
        {
            var go = new GameObject("BuildingsEditorUnderTest");
            var editor = go.AddComponent<BuildingsRuntimeEditor>();
            typeof(BuildingsRuntimeEditor)
                .GetField("_categoryFilter", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(editor, categoryFilter);
            return editor;
        }

        private static bool Matches(BuildingsRuntimeEditor editor, string assetPath)
        {
            var tpl = ScriptableObject.CreateInstance<BuildingTemplateData>();
            tpl.assetPath = assetPath;
            try
            {
                var m = typeof(BuildingsRuntimeEditor).GetMethod("MatchesCategoryFilter",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(m, "MatchesCategoryFilter is the picker's category gate; it must exist.");
                return (bool)m.Invoke(editor, new object[] { tpl });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tpl);
            }
        }

        [Test]
        public void AllTab_LetsEveryTemplateThrough()
        {
            var editor = MakeEditor("__all");
            try
            {
                Assert.IsTrue(Matches(editor, "Buildings/lights/lamp_post_classic"));
                Assert.IsTrue(Matches(editor, "Buildings/vegetation/tree_7"));
                Assert.IsTrue(Matches(editor, "Buildings/temples/catholic"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor.gameObject); }
        }

        [Test]
        public void UnsetFilter_BehavesLikeAll()
        {
            // The field starts as "" and stays that way until a tab fires, because AddTab's
            // implicit first activation happens before the callback is subscribed.
            var editor = MakeEditor("");
            try
            {
                Assert.IsTrue(Matches(editor, "Buildings/lights/lamp_post_classic"));
                Assert.IsTrue(Matches(editor, "Buildings/vegetation/tree_7"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor.gameObject); }
        }

        [Test]
        public void ACategoryTab_AdmitsOnlyItsOwnTemplates()
        {
            var editor = MakeEditor(Cat.Lights.ToString());
            try
            {
                Assert.IsTrue(Matches(editor, "Buildings/lights/brazier_iron_cage"));
                Assert.IsFalse(Matches(editor, "Buildings/vegetation/tree_7"));
                Assert.IsFalse(Matches(editor, "Buildings/market/crate_apples_mixed"));
                Assert.IsFalse(Matches(editor, "Buildings/temples/catholic"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor.gameObject); }
        }

        [Test]
        public void AGarbageFilterKey_FailsOpenInsteadOfEmptyingTheGrid()
        {
            // A key that no longer parses (a renamed enum member, a stale saved value) must
            // not silently hide the entire catalog.
            var editor = MakeEditor("NotACategory");
            try
            {
                Assert.IsTrue(Matches(editor, "Buildings/lights/lamp_post_classic"));
                Assert.IsTrue(Matches(editor, "Buildings/vegetation/tree_7"));
            }
            finally { UnityEngine.Object.DestroyImmediate(editor.gameObject); }
        }
    }
}
