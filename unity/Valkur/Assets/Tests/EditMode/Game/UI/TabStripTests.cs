using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Game.UI
{
    public class TabStripTests
    {
        private GameObject _rootGo;
        private TabStrip _tabStrip;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("TabStripRoot");
            var canvas = _rootGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _tabStrip = TabStrip.Create(_rootGo.transform, "TestTabStrip");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null)
                Object.DestroyImmediate(_rootGo);
        }

        [Test]
        public void Create_InitializesTabStrip()
        {
            Assert.IsNotNull(_tabStrip, "TabStrip should be created");
            Assert.AreEqual(0, _tabStrip.Count, "New TabStrip should have 0 tabs");
            Assert.AreEqual(-1, _tabStrip.ActiveIndex, "New TabStrip should have no active tab");
            Assert.IsNull(_tabStrip.ActiveKey, "New TabStrip should have no active key");
        }

        [Test]
        public void AddTab_IncreasesTabCount()
        {
            var content1 = new GameObject("Content1");
            content1.transform.SetParent(_rootGo.transform);

            _tabStrip.AddTab("tab1", "Tab 1", content1);

            Assert.AreEqual(1, _tabStrip.Count, "Should have 1 tab after AddTab");
            Assert.AreEqual(0, _tabStrip.ActiveIndex, "First tab should be auto-activated");
            Assert.AreEqual("tab1", _tabStrip.ActiveKey, "Active key should be 'tab1'");
        }

        [Test]
        public void AddTab_WithNullContent_WorksCorrectly()
        {
            _tabStrip.AddTab("tab1", "Tab 1", null);

            Assert.AreEqual(1, _tabStrip.Count);
            Assert.AreEqual(0, _tabStrip.ActiveIndex);
        }

        [Test]
        public void AddMultipleTabs_CreatesCorrectStructure()
        {
            var content1 = new GameObject("Content1");
            var content2 = new GameObject("Content2");
            var content3 = new GameObject("Content3");
            content1.transform.SetParent(_rootGo.transform);
            content2.transform.SetParent(_rootGo.transform);
            content3.transform.SetParent(_rootGo.transform);

            _tabStrip.AddTab("tab1", "Tab 1", content1);
            _tabStrip.AddTab("tab2", "Tab 2", content2);
            _tabStrip.AddTab("tab3", "Tab 3", content3);

            Assert.AreEqual(3, _tabStrip.Count, "Should have 3 tabs");
            Assert.AreEqual("tab1", _tabStrip.ActiveKey, "First tab should remain active");
        }

        [Test]
        public void SetActive_ByIndex_ChangesActiveTab()
        {
            var content1 = new GameObject("Content1");
            var content2 = new GameObject("Content2");
            content1.transform.SetParent(_rootGo.transform);
            content2.transform.SetParent(_rootGo.transform);

            _tabStrip.AddTab("tab1", "Tab 1", content1);
            _tabStrip.AddTab("tab2", "Tab 2", content2);

            _tabStrip.SetActive(1);

            Assert.AreEqual(1, _tabStrip.ActiveIndex, "Active index should be 1");
            Assert.AreEqual("tab2", _tabStrip.ActiveKey, "Active key should be 'tab2'");
            Assert.IsFalse(content1.activeSelf, "Content1 should be inactive");
            Assert.IsTrue(content2.activeSelf, "Content2 should be active");
        }

        [Test]
        public void SetActive_ByKey_ChangesActiveTab()
        {
            var content1 = new GameObject("Content1");
            var content2 = new GameObject("Content2");
            content1.transform.SetParent(_rootGo.transform);
            content2.transform.SetParent(_rootGo.transform);

            _tabStrip.AddTab("tab1", "Tab 1", content1);
            _tabStrip.AddTab("tab2", "Tab 2", content2);

            bool result = _tabStrip.SetActive("tab2");

            Assert.IsTrue(result, "SetActive by key should return true when key exists");
            Assert.AreEqual(1, _tabStrip.ActiveIndex);
            Assert.AreEqual("tab2", _tabStrip.ActiveKey);
        }

        [Test]
        public void SetActive_WithInvalidKey_ReturnsFalse()
        {
            _tabStrip.AddTab("tab1", "Tab 1", null);

            bool result = _tabStrip.SetActive("nonexistent");

            Assert.IsFalse(result, "SetActive should return false for invalid key");
            Assert.AreEqual("tab1", _tabStrip.ActiveKey, "Active tab should not change");
        }

        [Test]
        public void SetActive_WithInvalidIndex_DoesNothing()
        {
            _tabStrip.AddTab("tab1", "Tab 1", null);

            _tabStrip.SetActive(-1);
            Assert.AreEqual(0, _tabStrip.ActiveIndex, "Negative index should be ignored");

            _tabStrip.SetActive(999);
            Assert.AreEqual(0, _tabStrip.ActiveIndex, "Out-of-bounds index should be ignored");
        }

        [Test]
        public void TabChanged_EventFiresOnSetActive()
        {
            int eventIndex = -1;
            string eventKey = null;
            int eventFireCount = 0;

            _tabStrip.TabChanged += (idx, key) =>
            {
                eventIndex = idx;
                eventKey = key;
                eventFireCount++;
            };

            _tabStrip.AddTab("tab1", "Tab 1", null);
            _tabStrip.AddTab("tab2", "Tab 2", null);

            // Auto-activation of first tab fires event
            Assert.AreEqual(1, eventFireCount, "TabChanged should fire on first tab auto-activation");

            _tabStrip.SetActive(1);

            Assert.AreEqual(2, eventFireCount, "TabChanged should fire when changing tabs");
            Assert.AreEqual(1, eventIndex, "Event should report index 1");
            Assert.AreEqual("tab2", eventKey, "Event should report key 'tab2'");
        }

        [Test]
        public void TabActivation_TogglesContentVisibility()
        {
            var content1 = new GameObject("Content1");
            var content2 = new GameObject("Content2");
            var content3 = new GameObject("Content3");
            content1.transform.SetParent(_rootGo.transform);
            content2.transform.SetParent(_rootGo.transform);
            content3.transform.SetParent(_rootGo.transform);

            _tabStrip.AddTab("tab1", "Tab 1", content1);
            _tabStrip.AddTab("tab2", "Tab 2", content2);
            _tabStrip.AddTab("tab3", "Tab 3", content3);

            // Initially tab1 active
            Assert.IsTrue(content1.activeSelf);
            Assert.IsFalse(content2.activeSelf);
            Assert.IsFalse(content3.activeSelf);

            _tabStrip.SetActive(1);
            Assert.IsFalse(content1.activeSelf);
            Assert.IsTrue(content2.activeSelf);
            Assert.IsFalse(content3.activeSelf);

            _tabStrip.SetActive("tab3");
            Assert.IsFalse(content1.activeSelf);
            Assert.IsFalse(content2.activeSelf);
            Assert.IsTrue(content3.activeSelf);
        }

        [Test]
        public void Create_SetsUpHorizontalLayoutGroup()
        {
            var hlg = _tabStrip.GetComponent<HorizontalLayoutGroup>();

            Assert.IsNotNull(hlg, "TabStrip should have HorizontalLayoutGroup");
            Assert.AreEqual(2f, hlg.spacing, "Spacing should be 2");
            Assert.IsTrue(hlg.childForceExpandWidth, "Should force expand width");
            Assert.IsTrue(hlg.childControlWidth, "Should control child width");
            Assert.IsTrue(hlg.childControlHeight, "Should control child height");
        }

        [Test]
        public void Create_SetsPreferredHeight()
        {
            var le = _tabStrip.GetComponent<LayoutElement>();

            Assert.IsNotNull(le, "TabStrip should have LayoutElement");
            Assert.AreEqual(26f, le.preferredHeight, "Default height should match TabStrip.Create's default (26)");
        }

        // ── Wrapped (multi-row) mode ──────────────────────────────────────────────
        // Used by the Buildings Editor, whose 256 px panel cannot render nine tabs in a
        // single row. The invariant is that wrapping is purely a layout concern: keys,
        // activation and events must behave exactly as in the single-row strip.

        private TabStrip MakeWrapped(int columns, float rowHeight = 22f)
            => TabStrip.CreateWrapped(_rootGo.transform, "WrappedStrip", columns, rowHeight);

        [Test]
        public void CreateWrapped_UsesVerticalLayout_AndStartsOneRowTall()
        {
            var strip = MakeWrapped(columns: 3);

            Assert.IsNotNull(strip.GetComponent<VerticalLayoutGroup>(),
                "Wrapped strip stacks rows, so its root needs a VerticalLayoutGroup");
            Assert.IsNull(strip.GetComponent<HorizontalLayoutGroup>(),
                "The root must not also carry an HLG — the rows own the horizontal layout");
            Assert.AreEqual(22f, strip.GetComponent<LayoutElement>().preferredHeight,
                "An empty wrapped strip reserves exactly one row");
        }

        [Test]
        public void CreateWrapped_PacksTabsIntoRowsOfColumns()
        {
            var strip = MakeWrapped(columns: 3);
            for (int i = 0; i < 7; i++) strip.AddTab("t" + i, "T" + i, null);

            Assert.AreEqual(7, strip.Count, "Every tab must still be registered");
            Assert.AreEqual(3, strip.transform.childCount,
                "7 tabs at 3 per row occupy 3 rows");
            Assert.AreEqual(3, strip.transform.GetChild(0).childCount, "Row 0 is full");
            Assert.AreEqual(3, strip.transform.GetChild(1).childCount, "Row 1 is full");
            Assert.AreEqual(1, strip.transform.GetChild(2).childCount, "Row 2 holds the remainder");
        }

        [Test]
        public void CreateWrapped_GrowsItsLockedHeightPerRow()
        {
            var strip = MakeWrapped(columns: 3, rowHeight: 22f);
            var le = strip.GetComponent<LayoutElement>();

            for (int i = 0; i < 3; i++) strip.AddTab("t" + i, "T" + i, null);
            Assert.AreEqual(22f, le.preferredHeight, "One row of tabs is one row tall");

            strip.AddTab("t3", "T3", null);
            // Two rows plus the 2 px inter-row spacing.
            Assert.AreEqual(46f, le.preferredHeight,
                "Opening a second row must grow the reserved height, or the parent layout clips it");
            Assert.AreEqual(0f, le.flexibleHeight, "The strip must never stretch");
        }

        [Test]
        public void CreateWrapped_KeepsSelectionSemantics()
        {
            var strip = MakeWrapped(columns: 3);
            string lastKey = null;
            for (int i = 0; i < 5; i++) strip.AddTab("t" + i, "T" + i, null);
            strip.TabChanged += (_, key) => lastKey = key;

            Assert.AreEqual("t0", strip.ActiveKey, "First tab added is active, as in single-row mode");

            // t4 lives in the second row — activation must not care which row it is in.
            Assert.IsTrue(strip.SetActive("t4"), "SetActive(key) must find a tab in any row");
            Assert.AreEqual(4, strip.ActiveIndex);
            Assert.AreEqual("t4", lastKey, "TabChanged must fire with the new key");
        }

        [Test]
        public void CreateWrapped_WithZeroColumns_FallsBackToOnePerRow()
        {
            var strip = MakeWrapped(columns: 0);
            strip.AddTab("a", "A", null);
            strip.AddTab("b", "B", null);

            Assert.AreEqual(2, strip.transform.childCount,
                "A non-positive column count is clamped to 1, not treated as unlimited");
        }
    }
}
