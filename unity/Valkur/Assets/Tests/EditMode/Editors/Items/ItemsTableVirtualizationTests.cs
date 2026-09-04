using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Data;
using Valkur.Gameplay.Items;

namespace Valkur.Tests.EditMode.Editors.Items
{
    /// <summary>
    /// The Items table builds only the rows the viewport can show.
    ///
    /// <para>Measured before this existed: 38 columns x 180 items = 6,840 live widgets
    /// built on every <c>Activate()</c>, each a reasonable ~0.48 ms — a 3.5 s freeze on
    /// F7 in the game, and 178 s of a 343 s EditMode suite spent inside 37 Items tests
    /// (52 % of the suite in 0.5 % of the tests). Nothing in a cell was slow; the volume
    /// was. This fixture pins the shape that makes that impossible to reintroduce.</para>
    ///
    /// <para>The viewport height is set EXPLICITLY here. uGUI performs no layout in Edit
    /// Mode, so a stretched viewport reports whatever its default rect happens to be;
    /// giving it fixed anchors and a sizeDelta makes <c>rect.height</c> deterministic
    /// without a layout pass — the same reason the chat-panel tests sum authored
    /// <c>LayoutElement</c> values instead of measuring.</para>
    /// </summary>
    [TestFixture]
    public class ItemsTableVirtualizationTests
    {
        private const float ROW_H = 26f;           // mirrors ItemsRuntimeEditor.TABLE_ROW_H
        private const int   OVERSCAN = 4;          // mirrors TABLE_OVERSCAN_ROWS
        private const int   FALLBACK_ROWS = 24;    // mirrors TABLE_FALLBACK_VISIBLE_ROWS
        private const int   ITEMS = 200;

        private readonly List<GameObject> _scene = new List<GameObject>();
        private readonly List<Object> _runtimeAssets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            foreach (var a in _runtimeAssets) if (a != null) Object.DestroyImmediate(a);
            _runtimeAssets.Clear();
            ClearSingletonInstance<ItemsRuntimeEditor>();
        }

        // ── Reflection helpers (mirror the other Items fixtures) ─────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}");
            return null;
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, args); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        // ── Fixture ──────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the UI and injects <paramref name="items"/> BEFORE the first refresh, so
        /// the editor never loads the real catalog. <c>EnsureCatalog</c> returns early when
        /// <c>_allItems</c> is already set.
        /// </summary>
        private ItemsRuntimeEditor CreateEditorWith(int items, float viewportHeight)
        {
            ClearSingletonInstance<ItemsRuntimeEditor>();
            var go = new GameObject("TestItemsEditor_Virtual");
            _scene.Add(go);
            var ed = go.AddComponent<ItemsRuntimeEditor>();
            Invoke(ed, "BuildUI");

            var defs = new ItemDefinition[items];
            for (int i = 0; i < items; i++)
            {
                var d = ScriptableObject.CreateInstance<ItemDefinition>();
                d.itemId = $"item{i:D3}";
                d.displayName = "Item " + i;
                _runtimeAssets.Add(d);
                defs[i] = d;
            }
            Field(ed, "_allItems").SetValue(ed, defs);

            // Deterministic viewport rect with no layout pass: equal anchors make the
            // rect exactly sizeDelta whatever the parent measures.
            var scroll = (ScrollRect)Field(ed, "_tableScroll").GetValue(ed);
            var vp = scroll.viewport;
            vp.anchorMin = new Vector2(0f, 1f);
            vp.anchorMax = new Vector2(0f, 1f);
            vp.pivot     = new Vector2(0f, 1f);
            vp.sizeDelta = new Vector2(600f, viewportHeight);

            Invoke(ed, "RefreshPicker");
            Invoke(ed, "RefreshTable");
            return ed;
        }

        private static RectTransform Body(ItemsRuntimeEditor ed)
            => (RectTransform)Field(ed, "_tableBodyContent").GetValue(ed);

        private static bool HasRow(RectTransform body, int index)
        {
            string name = $"Row_item{index:D3}";
            for (int i = 0; i < body.childCount; i++)
                if (body.GetChild(i).name == name) return true;
            return false;
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Test]
        public void RefreshTable_BuildsOnlyTheViewportWindow()
        {
            const float viewportH = 10 * ROW_H;   // exactly ten rows tall
            var ed = CreateEditorWith(ITEMS, viewportH);
            var body = Body(ed);

            int maxExpected = 10 + OVERSCAN * 2 + 1;
            Assert.LessOrEqual(body.childCount, maxExpected,
                $"A 10-row viewport must realise at most {maxExpected} rows, not {body.childCount}. " +
                "Every row past the window is 38 widgets the player cannot see.");
            Assert.Less(body.childCount, ITEMS,
                "The whole list was built. This is the 3.5-second F7 freeze coming back.");
        }

        [Test]
        public void RefreshTable_SizesTheContentToTheWholeList()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);

            Assert.AreEqual(ITEMS * ROW_H, body.sizeDelta.y, 0.01f,
                "With no ContentSizeFitter, the content height IS the scrollbar range. It must " +
                "cover every row, realised or not, or the player cannot scroll to the ones that " +
                "do not exist yet.");
        }

        [Test]
        public void FirstChild_IsTheFirstRow_AfterRefresh()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);

            Assert.AreEqual("Row_item000", body.GetChild(0).name,
                "Rows are realised in index order, so GetChild(0) is row 0 — the contract the " +
                "column-config tests rely on.");
        }

        [Test]
        public void Rows_ArePlacedByIndex_NotStacked()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);

            for (int i = 0; i < body.childCount; i++)
            {
                var rt = (RectTransform)body.GetChild(i);
                int index = int.Parse(rt.name.Substring("Row_item".Length));
                Assert.AreEqual(-index * ROW_H, rt.anchoredPosition.y, 0.01f,
                    $"{rt.name} must sit at -index * ROW_H. A layout group stacking the realised " +
                    "window from the top would put row 150 where row 0 belongs.");
            }
        }

        [Test]
        public void Scrolling_RealisesTheNewWindow_AndDropsTheOld()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);

            Assert.IsTrue(HasRow(body, 0), "Row 0 exists before scrolling.");
            Assert.IsFalse(HasRow(body, 150), "Row 150 does not exist before scrolling.");

            // Scroll the content up by 150 rows and fire the ScrollRect callback the
            // editor subscribed in SetTableScrollRects.
            body.anchoredPosition = new Vector2(0f, 150 * ROW_H);
            Invoke(ed, "OnTableScrolled", Vector2.zero);

            Assert.IsTrue(HasRow(body, 150), "Row 150 must be realised once it is in view.");
            Assert.IsTrue(HasRow(body, 155), "Rows across the viewport must be realised.");
            Assert.IsFalse(HasRow(body, 0),
                "Row 0 must be dropped once it is far outside the window, or the table " +
                "grows into the full list again over one long scroll.");
            Assert.LessOrEqual(body.childCount, 10 + OVERSCAN * 2 + 1,
                "The realised count must stay bounded while scrolling.");
        }

        [Test]
        public void ScrollWithinTheSameWindow_TouchesNothing()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);
            var before = new List<GameObject>();
            for (int i = 0; i < body.childCount; i++) before.Add(body.GetChild(i).gameObject);

            // Half a row: no boundary crossed.
            body.anchoredPosition = new Vector2(0f, ROW_H * 0.5f);
            Invoke(ed, "OnTableScrolled", Vector2.zero);

            Assert.AreEqual(before.Count, body.childCount, "No rows may be added or removed.");
            for (int i = 0; i < before.Count; i++)
                Assert.IsTrue(before[i] != null, "No realised row may be destroyed and rebuilt.");
        }

        /// <summary>
        /// A viewport that reports no height is the Edit Mode normal, not an error. The
        /// fallback window keeps the table usable without ever becoming "all rows".
        /// </summary>
        [Test]
        public void UnknownViewportHeight_UsesTheFallbackWindow_NeverTheWholeList()
        {
            var ed = CreateEditorWith(ITEMS, 0f);
            var body = Body(ed);

            Assert.LessOrEqual(body.childCount, FALLBACK_ROWS + OVERSCAN * 2 + 1,
                "With an unmeasured viewport the table must realise the fallback window.");
            Assert.Less(body.childCount, ITEMS, "The fallback must never be the whole list.");
        }

        [Test]
        public void EmptyList_RealisesNothing_AndHasZeroHeight()
        {
            var ed = CreateEditorWith(0, 10 * ROW_H);
            var body = Body(ed);

            Assert.AreEqual(0, body.childCount);
            Assert.AreEqual(0f, body.sizeDelta.y, 0.01f);
        }

        [Test]
        public void ListShorterThanTheViewport_RealisesEveryRow()
        {
            var ed = CreateEditorWith(3, 10 * ROW_H);
            var body = Body(ed);

            Assert.AreEqual(3, body.childCount, "Three items, three rows — nothing to virtualise.");
            Assert.IsTrue(HasRow(body, 0) && HasRow(body, 1) && HasRow(body, 2));
        }

        /// <summary>
        /// The body must carry no layout group and no size fitter: either one would stack
        /// the realised window from the top and put row 150 where row 0 belongs, while
        /// the fitter would shrink the content to the window and make the rest
        /// unreachable. Pinned because both are the obvious thing to "add back".
        /// </summary>
        [Test]
        public void Body_HasNoLayoutGroupAndNoSizeFitter()
        {
            var ed = CreateEditorWith(ITEMS, 10 * ROW_H);
            var body = Body(ed);

            Assert.IsNull(body.GetComponent<LayoutGroup>(),
                "A layout group on the body would stack the realised window from the top.");
            Assert.IsNull(body.GetComponent<ContentSizeFitter>(),
                "A ContentSizeFitter would size the content to the realised window and " +
                "clip the scroll range to it.");
        }
    }
}
