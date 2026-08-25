using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// Coverage for the NEW internal bookkeeping added when
    /// <c>TileEditorUI.RefreshTilesetSelectionVisuals()</c> was rewritten from a
    /// full sweep of every registered picker slot (up to 3,045 catalog entries)
    /// into a diff against only what changed since the previous call.
    ///
    /// <c>TilesetPickerSelectionTests</c> already thoroughly covers the
    /// OBSERVABLE contract (which <c>Highlight.activeSelf</c> ends up true/false
    /// for Single/Rect/Multi selection, drag preview, clear, and a replaced Rect
    /// selection) — that public-facing behaviour did not change and is not
    /// duplicated here.
    ///
    /// This file targets the three NEW private dictionaries the diff needs
    /// (<c>_tilesetSlotByPos</c>, <c>_tilesetPrevHighlighted</c>,
    /// <c>_tilesetHighlightScratch</c>) and the specific new failure mode a diff
    /// introduces that a full sweep never could: a STALE entry surviving from a
    /// previous picker population that references a since-destroyed
    /// GameObject. A full sweep always re-derives from the CURRENT slot
    /// dictionary and can never touch a GameObject that isn't in it; a diff
    /// against a PREVIOUS snapshot can, unless that snapshot is cleared exactly
    /// when the grid is rebuilt — which is exactly what
    /// <c>ResetPickerSelectionState()</c> was extended to do.
    /// </summary>
    [TestFixture]
    public class TilesetPickerHighlightDiffTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();

            TileRegistry.Instance.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers (mirrors TilesetPickerSelectionTests) ────────

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetPrivate<T>(object obj, string name)
        {
            var f = GetField(obj, name);
            Assert.IsNotNull(f, $"Reflection: field '{name}' not found on {obj.GetType().Name}.");
            return (T)f.GetValue(obj);
        }

        private static void InvokePrivate(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            Assert.IsNotNull(m, $"Reflection failed: method '{method}' not found on {obj.GetType().Name}.");
            m.Invoke(obj, args);
        }

        private static int CountOf(object dict)
        {
            var prop = dict.GetType().GetProperty("Count");
            return (int)prop.GetValue(dict);
        }

        // ── Setup helpers (mirrors TilesetPickerSelectionTests) ─────────────

        private TileEditorUI CreateMinimalUI(TileEditorState.SelectMode mode)
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TileEditorUI_test");
            _sceneObjects.Add(go);
            var ui = go.AddComponent<TileEditorUI>();

            var state = new TileEditorState();
            state.CurrentSelectMode = mode;
            state.CurrentTool = TileEditorState.Tool.Select;
            GetField(ui, "_state").SetValue(ui, state);

            return ui;
        }

        private struct SlotHandle
        {
            public GameObject Slot;
            public GameObject Highlight;
            public TileCatalog.TileEntry Entry;
            public int Index;
            public int R;
            public int C;
            public Vector2Int Pos => new Vector2Int(C, R);
        }

        private List<SlotHandle> RegisterSlots(TileEditorUI ui, params (int r, int c)[] coords)
        {
            var handles = new List<SlotHandle>();
            for (int i = 0; i < coords.Length; i++)
            {
                var (r, c) = coords[i];

                var slotGo = new GameObject($"Slot_{r}_{c}");
                slotGo.transform.SetParent(ui.transform);
                _sceneObjects.Add(slotGo);

                var hlGo = new GameObject("DragHL");
                hlGo.transform.SetParent(slotGo.transform);
                hlGo.AddComponent<Image>();
                hlGo.SetActive(false);
                _sceneObjects.Add(hlGo);

                var tileSO = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
                tileSO.name = $"tile_{r}_{c}";
                _assets.Add(tileSO);

                var entry = new TileCatalog.TileEntry
                {
                    category = "test",
                    tileName = $"tile_{r}_{c}",
                    tile = tileSO,
                    preview = null,
                    gridR = r,
                    gridC = c,
                    uniqueId = i,
                    transparent = false,
                };

                InvokePrivate(ui, "RegisterPickerSlot", slotGo, r, c, entry, hlGo);

                handles.Add(new SlotHandle { Slot = slotGo, Highlight = hlGo, Entry = entry, Index = i, R = r, C = c });
            }
            return handles;
        }

        private void Down(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotDown", h.R, h.C, h.Index, h.Entry);

        private void Enter(TileEditorUI ui, int r, int c)
            => InvokePrivate(ui, "OnTilesetSlotEnter", r, c);

        private void Up(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotUp", h.Index, h.Entry);

        // ════════════════════════════════════════════════════════════════
        // 1. _tilesetSlotByPos — reverse lookup populated by RegisterPickerSlot
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void RegisterPickerSlot_PopulatesSlotByPos_KeyedByColRow()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (2, 3)); // r=2, c=3

            var slotByPos = GetPrivate<Dictionary<Vector2Int, GameObject>>(ui, "_tilesetSlotByPos");

            Assert.IsTrue(slotByPos.TryGetValue(new Vector2Int(3, 2), out var go),
                "_tilesetSlotByPos must be keyed (col, row) — the same convention as " +
                "_tilesetSelectedSlots — so the hot drag path can resolve 'the slot at " +
                "(col,row)' in O(1).");
            Assert.AreSame(slots[0].Slot, go);
        }

        // ════════════════════════════════════════════════════════════════
        // 2. ResetPickerSelectionState — clears the diffing dictionaries too
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void ResetPickerSelectionState_ClearsHighlightDiffingDictionaries()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (1, 1));
            Down(ui, slots[0]); Up(ui, slots[0]);

            Assert.Greater(CountOf(GetPrivate<object>(ui, "_tilesetSlotByPos")), 0, "Precondition.");
            Assert.Greater(CountOf(GetPrivate<object>(ui, "_tilesetPrevHighlighted")), 0, "Precondition.");

            InvokePrivate(ui, "ResetPickerSelectionState");

            Assert.AreEqual(0, CountOf(GetPrivate<object>(ui, "_tilesetSlotByPos")),
                "_tilesetSlotByPos must be cleared on category change — a stale (col,row) -> " +
                "GameObject entry pointing at a slot about to be destroyed must never survive " +
                "into the next grid.");
            Assert.AreEqual(0, CountOf(GetPrivate<object>(ui, "_tilesetPrevHighlighted")),
                "_tilesetPrevHighlighted must be cleared — otherwise a stale position from the " +
                "old category could be diffed against the new grid's slots and toggle a " +
                "destroyed GameObject (see the exception-based regression test below).");
            Assert.AreEqual(0, CountOf(GetPrivate<object>(ui, "_tilesetHighlightScratch")),
                "_tilesetHighlightScratch must also be reset defensively, even though it is " +
                "fully rebuilt on every RefreshTilesetSelectionVisuals() call.");
        }

        [Test]
        public void ResetPickerSelectionState_ThenRepopulate_PositionAbsentFromNewCategory_NeverTouchesDestroyedOldSlot()
        {
            // The sharpest form of the orphan risk: the OLD category had a highlighted
            // slot at (5,5); the NEW category never registers anything at (5,5) at all
            // (a smaller catalog page, a filtered category, etc). If either
            // _tilesetSlotByPos or _tilesetPrevHighlighted survived the category change
            // un-cleared, the next RefreshTilesetSelectionVisuals() call would look up
            // (5,5) in _tilesetSlotByPos, find the OLD (already-destroyed) GameObject,
            // and try to toggle its highlight -> MissingReferenceException.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var oldSlots = RegisterSlots(ui, (0, 0), (5, 5));
            Down(ui, oldSlots[1]); Up(ui, oldSlots[1]); // select (5,5)
            Assert.IsTrue(oldSlots[1].Highlight.activeSelf, "Precondition: (5,5) is highlighted.");

            // Category change, exactly as PopulateTileGrid does it in production:
            // reset the picker state FIRST, then destroy the old GameObjects.
            InvokePrivate(ui, "ResetPickerSelectionState");
            Object.DestroyImmediate(oldSlots[0].Slot);
            Object.DestroyImmediate(oldSlots[1].Slot);

            // New category registers only ONE slot, at a position the old category
            // never had a highlight on — (5,5) does not exist in the new grid at all.
            var newSlots = RegisterSlots(ui, (1, 1));

            Assert.DoesNotThrow(() =>
            {
                Down(ui, newSlots[0]);
                Up(ui, newSlots[0]);
            },
            "Selecting a slot in a freshly repopulated grid must never dereference a stale, " +
            "destroyed slot from the previous category. This is exactly the risk clearing " +
            "_tilesetSlotByPos / _tilesetPrevHighlighted in ResetPickerSelectionState exists " +
            "to prevent.");

            Assert.IsTrue(newSlots[0].Highlight.activeSelf, "New slot must highlight normally.");
        }

        // ════════════════════════════════════════════════════════════════
        // 3. Rect drag shrink — the diffing snapshot must match exactly, no orphans
        // ════════════════════════════════════════════════════════════════

        [Test]
        public void RectDrag_ShrinkBeforeRelease_PrevHighlightedDict_MatchesOnlyCurrentlyActiveCells()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1), (0, 2),
                (1, 0), (1, 1), (1, 2));

            Down(ui, slots[0]);   // anchor (0,0)
            Enter(ui, 1, 2);      // grows to the full 2x3 rect
            for (int i = 0; i < slots.Count; i++)
                Assert.IsTrue(slots[i].Highlight.activeSelf, $"Precondition: slot {i} lit mid-drag.");

            Enter(ui, 0, 0);      // shrinks back down to a single cell

            var prev = GetPrivate<Dictionary<Vector2Int, bool>>(ui, "_tilesetPrevHighlighted");
            Assert.AreEqual(1, prev.Count,
                "After shrinking the drag rect back to one cell, the diffing snapshot must " +
                "contain exactly that one cell — no orphans left over from the larger rect the " +
                "drag passed through.");
            Assert.IsTrue(prev.ContainsKey(new Vector2Int(0, 0)));

            Assert.IsTrue(slots[0].Highlight.activeSelf, "The one remaining cell must stay lit.");
            for (int i = 1; i < slots.Count; i++)
                Assert.IsFalse(slots[i].Highlight.activeSelf,
                    $"Slot {i} must be explicitly deactivated after the rect shrank away from " +
                    "it — an orphan here is the exact bug class this diffing rewrite must not " +
                    "reintroduce.");
        }
    }
}
