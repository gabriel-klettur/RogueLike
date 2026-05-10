using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// EditMode tests for the F8 Tile Editor picker selection model
    /// (TileEditorUI.TilesetView). Specifically, tests the Single / Rect /
    /// Multi dispatch driven by <see cref="TileEditorState.CurrentSelectMode"/>
    /// when the user clicks / drags slots in the TILES grid.
    ///
    /// Coverage:
    ///   • SINGLE mode — click replaces selection, sets active brush.
    ///   • RECT mode   — drag fills a rectangle, release commits.
    ///   • MULTI mode  — clicks toggle individual slots, accumulating.
    ///   • Cross-mode  — switching modes preserves the selection.
    ///   • Clipboard   — built correctly from rectangular and dispersed sets.
    ///   • Visuals     — DragHL highlight overlay activates / deactivates as
    ///                   the selection state changes.
    ///   • Clear       — both ClearTilesetSelection() and the implicit reset
    ///                   in PopulateTileGrid wipe the picker state.
    ///
    /// Pattern: reflection helpers mirror MapEditorTests / TerrainPainterTests
    /// so tests stay green even though most of the picker selection state is
    /// internal to TileEditorUI.
    /// </summary>
    [TestFixture]
    public class TilesetPickerSelectionTests
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

            // TileRegistry caches Tile instances by sprite name across tests.
            // Without this, a Tile we created in test N could be returned by
            // TerrainTileResolver in test N+1 with a destroyed sprite ref.
            TileRegistry.Instance.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ──────────────────────────────────────────────

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
            return f != null ? (T)f.GetValue(obj) : default;
        }

        private static void SetPrivate(object obj, string name, object value)
        {
            var f = GetField(obj, name);
            if (f != null) f.SetValue(obj, value);
        }

        private static void InvokePrivate(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            Assert.IsNotNull(m,
                $"Reflection failed: method '{method}' not found on {obj.GetType().Name}.");
            m.Invoke(obj, args);
        }

        // ── Setup helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a TileEditorUI MonoBehaviour with the minimum state needed
        /// to drive the picker selection handlers. We deliberately skip
        /// <c>Initialize</c> — building the full canvas brings in dozens of
        /// unrelated dependencies; the selection logic only touches
        /// <c>_state</c>, <c>_tilesetSelectedSlots</c>, <c>_tilesetSlotInfo</c>,
        /// <c>_tilesetSlotHighlight</c>. Other fields default to null and the
        /// production code null-checks them gracefully.
        /// </summary>
        private TileEditorUI CreateMinimalUI(TileEditorState.SelectMode mode)
        {
            LogAssert.ignoreFailingMessages = true;

            var go = new GameObject("TileEditorUI_test");
            _sceneObjects.Add(go);
            var ui = go.AddComponent<TileEditorUI>();

            var state = new TileEditorState();
            state.CurrentSelectMode = mode;
            // SelectMode panel only matters for the map; the picker reads
            // CurrentSelectMode directly. Keeping CurrentTool=Select mirrors
            // the live UX where the user activated SelectModes via Tools.
            state.CurrentTool = TileEditorState.Tool.Select;
            SetPrivate(ui, "_state", state);
            // _catalog stays null — IsCurrentCategoryTilesheet returns false,
            // which is fine for selection tests (we register slots manually).

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
            /// <summary>The (col, row) key used by _tilesetSelectedSlots.</summary>
            public Vector2Int Pos => new Vector2Int(C, R);
        }

        /// <summary>
        /// Builds N test slots, each a GameObject with a child <c>DragHL</c>
        /// Image overlay (initially disabled), and registers them via the
        /// production <c>RegisterPickerSlot</c> API. Returns handles in the
        /// same order the coordinates were supplied.
        /// </summary>
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
                    category    = "test",
                    tileName    = $"tile_{r}_{c}",
                    tile        = tileSO,
                    preview     = null,
                    gridR       = r,
                    gridC       = c,
                    uniqueId    = i,
                    transparent = false,
                };

                // RegisterPickerSlot is internal; reflection bypasses that.
                InvokePrivate(ui, "RegisterPickerSlot", slotGo, r, c, entry, hlGo);

                handles.Add(new SlotHandle
                {
                    Slot = slotGo, Highlight = hlGo, Entry = entry,
                    Index = i, R = r, C = c,
                });
            }
            return handles;
        }

        private void Down(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotDown", h.R, h.C, h.Index, h.Entry);

        private void Enter(TileEditorUI ui, int r, int c)
            => InvokePrivate(ui, "OnTilesetSlotEnter", r, c);

        private void Up(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotUp", h.Index, h.Entry);

        private HashSet<Vector2Int> Selected(TileEditorUI ui)
            => GetPrivate<HashSet<Vector2Int>>(ui, "_tilesetSelectedSlots");

        private TileEditorState State(TileEditorUI ui)
            => GetPrivate<TileEditorState>(ui, "_state");

        // ─── SINGLE ─────────────────────────────────────────────────────────

        [Test]
        public void Single_Click_AddsExactlyOneTileToSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (1, 0));

            Down(ui, slots[1]);
            Up(ui, slots[1]);

            var sel = Selected(ui);
            Assert.AreEqual(1, sel.Count, "SINGLE click should leave exactly one slot selected.");
            Assert.IsTrue(sel.Contains(slots[1].Pos),
                "Selection must contain the (col, row) of the clicked slot.");
        }

        [Test]
        public void Single_SecondClick_ReplacesPreviousSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (1, 0));

            Down(ui, slots[0]);
            Up(ui, slots[0]);
            Down(ui, slots[2]);
            Up(ui, slots[2]);

            var sel = Selected(ui);
            Assert.AreEqual(1, sel.Count, "SINGLE never accumulates: second click must replace.");
            Assert.IsTrue(sel.Contains(slots[2].Pos), "Set should now hold only the second click.");
            Assert.IsFalse(sel.Contains(slots[0].Pos), "First-click slot must have been removed.");
        }

        [Test]
        public void Single_Click_SetsActiveBrushIndex()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[1]);
            Up(ui, slots[1]);

            int activeIdx = GetPrivate<int>(ui, "_selectedSlotIndex");
            Assert.AreEqual(slots[1].Index, activeIdx,
                "SINGLE click must set the active brush slot index so subsequent " +
                "Brush-tool paints use the picked tile.");
        }

        [Test]
        public void Single_Click_BuildsOneByOneClipboard()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[1]);
            Up(ui, slots[1]);

            var clip = State(ui).Clipboard;
            Assert.IsNotNull(clip, "SINGLE click must populate the clipboard.");
            Assert.AreEqual(1, clip.Width,  "Clipboard width should be 1.");
            Assert.AreEqual(1, clip.Height, "Clipboard height should be 1.");
            Assert.AreSame(slots[1].Entry.tile, clip.Tiles[0, 0],
                "The single clipboard cell must hold the clicked entry's tile.");
        }

        // ─── MULTI ──────────────────────────────────────────────────────────

        [Test]
        public void Multi_Clicks_AccumulateInTheSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (0, 2));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[1]); Up(ui, slots[1]);
            Down(ui, slots[2]); Up(ui, slots[2]);

            var sel = Selected(ui);
            Assert.AreEqual(3, sel.Count, "MULTI mode must accumulate every distinct click.");
            CollectionAssert.AreEquivalent(
                new[] { slots[0].Pos, slots[1].Pos, slots[2].Pos },
                sel,
                "Selection must contain the union of all clicked slots.");
        }

        [Test]
        public void Multi_ClickingSameTileTwice_TogglesItOff()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[1]); Up(ui, slots[1]);
            Down(ui, slots[0]); Up(ui, slots[0]); // toggle slot[0] off

            var sel = Selected(ui);
            Assert.AreEqual(1, sel.Count, "Toggling one slot off should leave one entry.");
            Assert.IsTrue(sel.Contains(slots[1].Pos), "Slot 1 should still be selected.");
            Assert.IsFalse(sel.Contains(slots[0].Pos), "Slot 0 should have been toggled off.");
        }

        [Test]
        public void Multi_LastClick_BecomesActiveBrush()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (0, 2));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[2]); Up(ui, slots[2]);

            int activeIdx = GetPrivate<int>(ui, "_selectedSlotIndex");
            Assert.AreEqual(slots[2].Index, activeIdx,
                "Active brush in MULTI mode must follow the most recently clicked slot, " +
                "so quick switches to Brush tool start with the user's last pick.");
        }

        [Test]
        public void Multi_Dispersed_ProducesBoundingBoxClipboardWithNullsInGaps()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            // Pick three corners of a 3×3 region so the bbox has gaps.
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1), (0, 2),
                (1, 0), (1, 1), (1, 2),
                (2, 0), (2, 1), (2, 2));

            // Select (0,0), (0,2), (2,1) — non-rectangular set.
            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[2]); Up(ui, slots[2]);
            Down(ui, slots[7]); Up(ui, slots[7]);

            var clip = State(ui).Clipboard;
            Assert.IsNotNull(clip);
            Assert.AreEqual(3, clip.Width,  "Bbox width covers cols 0..2.");
            Assert.AreEqual(3, clip.Height, "Bbox height covers rows 0..2.");

            // dy = (rMax - r). rMax=2, so r=0 → dy=2, r=1 → dy=1, r=2 → dy=0.
            Assert.AreSame(slots[0].Entry.tile, clip.Tiles[0, 2], "(r=0,c=0) → Tiles[0,2]");
            Assert.AreSame(slots[2].Entry.tile, clip.Tiles[2, 2], "(r=0,c=2) → Tiles[2,2]");
            Assert.AreSame(slots[7].Entry.tile, clip.Tiles[1, 0], "(r=2,c=1) → Tiles[1,0]");

            // The unselected positions in the bbox must be null so Paste skips them.
            Assert.IsNull(clip.Tiles[1, 2], "(r=0,c=1) was not selected → null.");
            Assert.IsNull(clip.Tiles[0, 1], "(r=1,c=0) was not selected → null.");
            Assert.IsNull(clip.Tiles[2, 0], "(r=2,c=2) was not selected → null.");
        }

        // ─── RECT ───────────────────────────────────────────────────────────

        [Test]
        public void Rect_DragRectangle_FillsAllCellsInBbox()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1), (0, 2),
                (1, 0), (1, 1), (1, 2));

            // Drag from (r=0,c=0) to (r=1,c=2) — covers all 6 cells.
            Down(ui, slots[0]);
            Enter(ui, 1, 2);
            Up(ui, slots[5]);

            var sel = Selected(ui);
            Assert.AreEqual(6, sel.Count, "RECT drag must cover every cell in the bbox.");
            for (int r = 0; r <= 1; r++)
            for (int c = 0; c <= 2; c++)
                Assert.IsTrue(sel.Contains(new Vector2Int(c, r)),
                    $"Bbox cell ({c},{r}) missing after drag.");
        }

        [Test]
        public void Rect_OneCell_ResultsInSingleSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            // Press + release on the same slot, no Enter in between.
            Down(ui, slots[0]);
            Up(ui, slots[0]);

            var sel = Selected(ui);
            Assert.AreEqual(1, sel.Count, "Rect drag of 1 cell collapses to one selection.");
            Assert.IsTrue(sel.Contains(slots[0].Pos));
        }

        [Test]
        public void Rect_OneCell_SetsActiveBrush_LegacyConvenience()
        {
            // Explicit guarantee: a 1×1 RECT release behaves as a quick brush
            // pick. Without this the user would be stuck in Rect mode with no
            // way to switch to single-tile painting from one click.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[1]);
            Up(ui, slots[1]);

            int activeIdx = GetPrivate<int>(ui, "_selectedSlotIndex");
            Assert.AreEqual(slots[1].Index, activeIdx);
        }

        [Test]
        public void Rect_ReleaseReplacesPreviousSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1), (0, 2),
                (1, 0), (1, 1), (1, 2));

            // First drag: (0,0) → (0,2).
            Down(ui, slots[0]); Enter(ui, 0, 2); Up(ui, slots[2]);
            Assert.AreEqual(3, Selected(ui).Count, "First drag fills 3 cells.");

            // Second drag: (1,0) → (1,1) — must REPLACE, not union.
            Down(ui, slots[3]); Enter(ui, 1, 1); Up(ui, slots[4]);

            var sel = Selected(ui);
            Assert.AreEqual(2, sel.Count, "Second drag must REPLACE the first selection.");
            Assert.IsTrue(sel.Contains(new Vector2Int(0, 1)));
            Assert.IsTrue(sel.Contains(new Vector2Int(1, 1)));
            Assert.IsFalse(sel.Contains(new Vector2Int(0, 0)),
                "First-drag cell must be cleared on second drag's release.");
        }

        [Test]
        public void Rect_ProducesContiguousClipboard()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1),
                (1, 0), (1, 1));

            Down(ui, slots[0]); Enter(ui, 1, 1); Up(ui, slots[3]);

            var clip = State(ui).Clipboard;
            Assert.IsNotNull(clip);
            Assert.AreEqual(2, clip.Width);
            Assert.AreEqual(2, clip.Height);
            // Every cell of a contiguous RECT clipboard is non-null.
            for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                Assert.IsNotNull(clip.Tiles[dx, dy],
                    $"Tiles[{dx},{dy}] must be populated for a fully-filled RECT.");
        }

        // ─── Cross-mode ─────────────────────────────────────────────────────

        [Test]
        public void ChangingModeMidSelection_DoesNotWipeIt()
        {
            // The picker keeps its selection across Single→Multi switches so the
            // user can refine a selection with a different gesture.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[1]); Up(ui, slots[1]);
            Assert.AreEqual(2, Selected(ui).Count);

            // User flips the mode to Rect — selection should survive.
            State(ui).CurrentSelectMode = TileEditorState.SelectMode.Rect;

            Assert.AreEqual(2, Selected(ui).Count,
                "Mode change alone must not clear the picker selection.");
        }

        [Test]
        public void NonRectModes_IgnoreEnterAndDragEvents()
        {
            // PointerEnter on a peer slot is a no-op outside Rect mode — it
            // would be confusing if accidental hover-during-click in Single
            // or Multi started building a rect.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (1, 0));

            Down(ui, slots[0]);
            Enter(ui, 0, 1);  // would extend a rect in Rect mode
            Enter(ui, 1, 0);
            Up(ui, slots[0]);

            var sel = Selected(ui);
            Assert.AreEqual(1, sel.Count,
                "MULTI must only react to Down/Up, never to PointerEnter.");
            Assert.IsTrue(sel.Contains(slots[0].Pos));
        }

        // ─── Visual highlights ──────────────────────────────────────────────

        [Test]
        public void Highlight_Activates_OnSelectedSlots_Single()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (1, 0));

            Down(ui, slots[1]); Up(ui, slots[1]);

            Assert.IsFalse(slots[0].Highlight.activeSelf, "Slot 0 must remain hidden.");
            Assert.IsTrue (slots[1].Highlight.activeSelf, "Selected slot must show its overlay.");
            Assert.IsFalse(slots[2].Highlight.activeSelf, "Slot 2 must remain hidden.");
        }

        [Test]
        public void Highlight_DragPreview_ShowsAllInRectMidDrag()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);
            var slots = RegisterSlots(ui,
                (0, 0), (0, 1), (0, 2),
                (1, 0), (1, 1), (1, 2));

            Down(ui, slots[0]);
            Enter(ui, 1, 2); // mid-drag — release NOT yet fired

            // Every slot inside the in-flight rect should show its highlight.
            Assert.IsTrue(slots[0].Highlight.activeSelf);
            Assert.IsTrue(slots[2].Highlight.activeSelf);
            Assert.IsTrue(slots[5].Highlight.activeSelf);
        }

        [Test]
        public void Highlight_Hidden_AfterClearTilesetSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[1]); Up(ui, slots[1]);
            Assert.IsTrue(slots[0].Highlight.activeSelf);
            Assert.IsTrue(slots[1].Highlight.activeSelf);

            ui.ClearTilesetSelection();

            Assert.IsFalse(slots[0].Highlight.activeSelf,
                "ClearTilesetSelection must hide every slot's overlay.");
            Assert.IsFalse(slots[1].Highlight.activeSelf);
        }

        // ─── Clear / Reset ──────────────────────────────────────────────────

        [Test]
        public void ClearTilesetSelection_EmptiesSelectionAndNullsClipboard()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            Down(ui, slots[0]); Up(ui, slots[0]);
            Down(ui, slots[1]); Up(ui, slots[1]);
            Assert.AreEqual(2, Selected(ui).Count);
            Assert.IsNotNull(State(ui).Clipboard);

            ui.ClearTilesetSelection();

            Assert.AreEqual(0, Selected(ui).Count, "Selection set must be empty.");
            Assert.IsNull(State(ui).Clipboard,
                "Clipboard must be nulled so Paste reflects the empty selection.");
        }

        [Test]
        public void ResetPickerSelectionState_DropsSlotInfoAndDragState()
        {
            // ResetPickerSelectionState is what PopulateTileGrid calls on
            // category change to start from a clean slate — verify it wipes
            // every internal map, not just the public selection set.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));
            Down(ui, slots[0]); Up(ui, slots[0]);

            InvokePrivate(ui, "ResetPickerSelectionState");

            Assert.AreEqual(0, Selected(ui).Count);

            var slotInfo = GetField(ui, "_tilesetSlotInfo").GetValue(ui);
            var slotInfoCount = (int)slotInfo.GetType().GetProperty("Count").GetValue(slotInfo);
            Assert.AreEqual(0, slotInfoCount,
                "Slot info dictionary must be cleared so stale (r,c) keys " +
                "from the previous category cannot collide with the next one.");

            var slotHighlight = GetField(ui, "_tilesetSlotHighlight").GetValue(ui);
            var slotHighlightCount = (int)slotHighlight.GetType().GetProperty("Count").GetValue(slotHighlight);
            Assert.AreEqual(0, slotHighlightCount, "Highlight dictionary must also be cleared.");

            var dragStart = GetPrivate<Vector2Int?>(ui, "_tilesetDragStart");
            Assert.IsFalse(dragStart.HasValue, "_tilesetDragStart must reset to null.");
        }

        // ─── Transparent entries ────────────────────────────────────────────

        [Test]
        public void TransparentEntries_AreSkipped_FromClipboard()
        {
            // Tilesheet manifests can mark cells as transparent so Paste skips
            // those positions on the map. The selection set still contains them
            // (geometrically intact); the clipboard just carries null there.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Rect);

            // Build slots manually — the second one is transparent.
            var go0 = new GameObject("Slot_0_0"); go0.transform.SetParent(ui.transform); _sceneObjects.Add(go0);
            var hl0 = new GameObject("DragHL");   hl0.transform.SetParent(go0.transform);
            hl0.AddComponent<Image>(); hl0.SetActive(false); _sceneObjects.Add(hl0);

            var go1 = new GameObject("Slot_0_1"); go1.transform.SetParent(ui.transform); _sceneObjects.Add(go1);
            var hl1 = new GameObject("DragHL");   hl1.transform.SetParent(go1.transform);
            hl1.AddComponent<Image>(); hl1.SetActive(false); _sceneObjects.Add(hl1);

            var tile0 = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>(); _assets.Add(tile0);
            var tile1 = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>(); _assets.Add(tile1);

            var entry0 = new TileCatalog.TileEntry { tile = tile0, gridR = 0, gridC = 0, transparent = false };
            var entry1 = new TileCatalog.TileEntry { tile = tile1, gridR = 0, gridC = 1, transparent = true };

            InvokePrivate(ui, "RegisterPickerSlot", go0, 0, 0, entry0, hl0);
            InvokePrivate(ui, "RegisterPickerSlot", go1, 0, 1, entry1, hl1);

            // Drag covering both cells.
            InvokePrivate(ui, "OnTilesetSlotDown", 0, 0, 0, entry0);
            InvokePrivate(ui, "OnTilesetSlotEnter", 0, 1);
            InvokePrivate(ui, "OnTilesetSlotUp", 1, entry1);

            var clip = State(ui).Clipboard;
            Assert.IsNotNull(clip);
            Assert.AreEqual(2, clip.Width);
            Assert.AreEqual(1, clip.Height);
            Assert.AreSame(tile0, clip.Tiles[0, 0],  "Opaque tile must reach the clipboard.");
            Assert.IsNull(clip.Tiles[1, 0], "Transparent entries must be skipped → null in clipboard.");
        }
    }
}
