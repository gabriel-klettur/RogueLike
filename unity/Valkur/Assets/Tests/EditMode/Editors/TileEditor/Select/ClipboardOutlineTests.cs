using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// EditMode tests for the "clipboard yellow outline" feature (map side + picker side).
    ///
    /// Map side — <c>TileEditorManager._copiedMapCells</c> + <c>TileEditorGridOverlay.SetCopiedCells</c>:
    ///   • <c>OnCopyClicked</c> snapshots the selected cells.
    ///   • A second Copy replaces the snapshot.
    ///   • <c>ClearSelection</c> clears the snapshot.
    ///   • Deactivation clears the snapshot.
    ///
    /// Picker side — <c>TileEditorUI._tilesetCopiedSlots</c> + <c>CopyHL</c> overlay:
    ///   • <c>CommitTilesetSelection</c> snapshots <c>_tilesetSelectedSlots</c>.
    ///   • A second commit replaces the snapshot.
    ///   • <c>ClearTilesetSelection</c> clears both sets.
    ///   • <c>ResetPickerSelectionState</c> (category change) clears the copy set.
    ///
    /// Independence — map outline is unaffected by picker commit and vice-versa.
    ///
    /// Pattern: uses the same reflection helpers as <see cref="TilesetPickerSelectionTests"/>
    /// and <see cref="PickerRectPasteAnchorTests"/> so no production API is widened.
    /// </summary>
    [TestFixture]
    public class ClipboardOutlineTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _assets = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            // Force-null TileEditorManager._instance before every test so the
            // Singleton's Awake takes the "first instance" branch on the test's
            // own AddComponent. Without this, leaked _instance from a previous
            // test (or even from a different test fixture) makes Awake call
            // Destroy(gameObject) — illegal in Edit mode and noisy in the
            // console even though it doesn't fail the assertion.
            SetSingletonInstance(null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();
            foreach (var a in _assets)
                if (a != null) Object.DestroyImmediate(a);
            _assets.Clear();

            // Defence in depth: the SingletonMonoBehaviour.OnDestroy normally
            // sets _instance = null, but if any test's manager wasn't tracked
            // in _sceneObjects (or DestroyImmediate didn't fire OnDestroy for
            // any reason), leave the field clean for the next fixture.
            SetSingletonInstance(null);

            TileRegistry.Instance.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ─────────────────────────────────────────────────

        private static T GetField<T>(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return (T)f.GetValue(obj);
                t = t.BaseType;
            }
            Assert.Fail($"Reflection: field '{name}' not found on {obj.GetType().Name}.");
            return default;
        }

        private static void SetField(object obj, string name, object value)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) { f.SetValue(obj, value); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Reflection: field '{name}' not found on {obj.GetType().Name}.");
        }

        private static void InvokePrivate(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo mi = null;
            while (t != null && mi == null)
            {
                foreach (var m in t.GetMethods(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (m.Name != method) continue;
                    if (m.GetParameters().Length != args.Length) continue;
                    mi = m; break;
                }
                t = t.BaseType;
            }
            Assert.IsNotNull(mi,
                $"Reflection: method '{method}'({args.Length} args) not found on {obj.GetType().Name}.");
            mi.Invoke(obj, args);
        }

        // ── Map-side helpers ─────────────────────────────────────────────────

        private TileEditorManager NewManager()
        {
            var go = new GameObject("TileEditorManager_ClipboardTest");
            _sceneObjects.Add(go);
            return go.AddComponent<TileEditorManager>();
        }

        /// <summary>
        /// Attaches a minimal WorldGridBuilder + 9 Tilemaps and wires the manager's
        /// fields so <c>GetCurrentTilemap()</c> resolves without a NullRef.
        /// Returns the Ground tilemap.
        /// </summary>
        private Tilemap AttachWorldGrid(TileEditorManager manager)
        {
            var gridGo = new GameObject("WorldGrid");
            _sceneObjects.Add(gridGo);
            gridGo.transform.SetParent(manager.transform, false);
            gridGo.AddComponent<Grid>();
            var wgb = gridGo.AddComponent<WorldGridBuilder>();
            SetField(wgb, "_grid", gridGo.GetComponent<Grid>());

            Tilemap ground = null;
            for (int i = 0; i < 9; i++)
            {
                var layer = (TilemapLayerSetup.TilemapLayer)i;
                var tmGo = new GameObject(layer.ToString());
                _sceneObjects.Add(tmGo);
                tmGo.transform.SetParent(gridGo.transform, false);
                var tm = tmGo.AddComponent<Tilemap>();
                tmGo.AddComponent<TilemapRenderer>();
                if (layer == TilemapLayerSetup.TilemapLayer.Ground) ground = tm;
            }
            SetField(manager, "worldGridBuilder", wgb);

            // Wire undo system so OnCopyClicked/OnCutClicked don't NRE.
            var undoField = typeof(TileEditorManager).GetField(
                "_undo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (undoField != null && undoField.GetValue(manager) == null)
                undoField.SetValue(manager, new TileEditorUndoSystem());

            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
            return ground;
        }

        /// <summary>Force <c>_state.SelectedCells</c> to contain the given cells.</summary>
        private static void SetSelectedCells(TileEditorManager manager,
            params Vector3Int[] cells)
        {
            manager.State.SelectedCells.Clear();
            foreach (var c in cells) manager.State.SelectedCells.Add(c);
        }

        /// <summary>Return the manager's internal <c>_copiedMapCells</c> set.</summary>
        private static HashSet<Vector3Int> CopiedMapCells(TileEditorManager manager)
            => GetField<HashSet<Vector3Int>>(manager, "_copiedMapCells");

        // ── Picker-side helpers ──────────────────────────────────────────────

        private TileEditorUI CreateMinimalUI(TileEditorState.SelectMode mode)
        {
            var go = new GameObject("TileEditorUI_CopyHL");
            _sceneObjects.Add(go);
            var ui = go.AddComponent<TileEditorUI>();
            var state = new TileEditorState();
            state.CurrentSelectMode = mode;
            state.CurrentTool = TileEditorState.Tool.Select;
            SetField(ui, "_state", state);
            return ui;
        }

        private struct SlotHandle
        {
            public GameObject Slot;
            public GameObject SelectHL;   // DragHL — green selection
            public GameObject CopyHL;     // yellow copy indicator
            public TileCatalog.TileEntry Entry;
            public int Index;
            public int R;
            public int C;
            public Vector2Int Pos => new Vector2Int(C, R);
        }

        /// <summary>
        /// Registers N slots with BOTH a DragHL (green select) and a CopyHL (yellow copy)
        /// overlay, mirroring what the production <c>PopulateTilesheetSlots</c> does.
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

                var cGo = new GameObject("CopyHL");
                cGo.transform.SetParent(slotGo.transform);
                cGo.AddComponent<Image>();
                cGo.SetActive(false);
                _sceneObjects.Add(cGo);

                var tileSO = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
                tileSO.name = $"tile_{r}_{c}";
                _assets.Add(tileSO);
                var entry = new TileCatalog.TileEntry
                {
                    category = "test", tileName = $"tile_{r}_{c}", tile = tileSO,
                    gridR = r, gridC = c, uniqueId = i, transparent = false,
                };

                InvokePrivate(ui, "RegisterPickerSlot", slotGo, r, c, entry, hlGo);
                InvokePrivate(ui, "RegisterPickerSlotCopyHighlight", slotGo, cGo);

                handles.Add(new SlotHandle
                {
                    Slot = slotGo, SelectHL = hlGo, CopyHL = cGo,
                    Entry = entry, Index = i, R = r, C = c,
                });
            }
            return handles;
        }

        private void SlotDown(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotDown", h.R, h.C, h.Index, h.Entry);

        private void SlotUp(TileEditorUI ui, SlotHandle h)
            => InvokePrivate(ui, "OnTilesetSlotUp", h.Index, h.Entry);

        private HashSet<Vector2Int> CopiedSlots(TileEditorUI ui)
            => GetField<HashSet<Vector2Int>>(ui, "_tilesetCopiedSlots");

        // ═══════════════════════════════════════════════════════════════════════
        // MAP SIDE
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Map_OnCopyClicked_SnapshotsCopiedMapCells()
        {
            var manager = NewManager();
            AttachWorldGrid(manager);

            SetSelectedCells(manager,
                new Vector3Int(1, 2, 0),
                new Vector3Int(3, 4, 0));

            InvokePrivate(manager, "OnCopyClicked");

            var copied = CopiedMapCells(manager);
            Assert.AreEqual(2, copied.Count, "Copy must snapshot exactly the selected cells.");
            Assert.IsTrue(copied.Contains(new Vector3Int(1, 2, 0)));
            Assert.IsTrue(copied.Contains(new Vector3Int(3, 4, 0)));
        }

        [Test]
        public void Map_OnCutClicked_SnapshotsCopiedMapCells()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);

            var tile = ScriptableObject.CreateInstance<Tile>();
            _assets.Add(tile);
            tilemap.SetTile(new Vector3Int(5, 5, 0), tile);

            SetSelectedCells(manager, new Vector3Int(5, 5, 0));

            InvokePrivate(manager, "OnCutClicked");

            var copied = CopiedMapCells(manager);
            Assert.AreEqual(1, copied.Count, "Cut must also snapshot the copied map cells.");
            Assert.IsTrue(copied.Contains(new Vector3Int(5, 5, 0)));
        }

        [Test]
        public void Map_SecondCopy_ReplacesCopiedMapCells()
        {
            var manager = NewManager();
            AttachWorldGrid(manager);

            SetSelectedCells(manager, new Vector3Int(0, 0, 0));
            InvokePrivate(manager, "OnCopyClicked");
            Assert.AreEqual(1, CopiedMapCells(manager).Count);

            SetSelectedCells(manager, new Vector3Int(7, 8, 0), new Vector3Int(9, 10, 0));
            InvokePrivate(manager, "OnCopyClicked");

            var copied = CopiedMapCells(manager);
            Assert.AreEqual(2, copied.Count, "Second Copy must REPLACE the first snapshot.");
            Assert.IsFalse(copied.Contains(new Vector3Int(0, 0, 0)),
                "The cell from the first copy must no longer be in the snapshot.");
            Assert.IsTrue(copied.Contains(new Vector3Int(7, 8, 0)));
            Assert.IsTrue(copied.Contains(new Vector3Int(9, 10, 0)));
        }

        [Test]
        public void Map_ClearSelection_ClearsCopiedMapCells()
        {
            var manager = NewManager();
            AttachWorldGrid(manager);

            SetSelectedCells(manager, new Vector3Int(1, 1, 0));
            InvokePrivate(manager, "OnCopyClicked");
            Assert.AreEqual(1, CopiedMapCells(manager).Count, "Pre-condition: one cell copied.");

            manager.ClearSelection();

            Assert.AreEqual(0, CopiedMapCells(manager).Count,
                "ClearSelection must wipe the map-side clipboard outline.");
        }

        [Test]
        public void Map_Deactivate_ClearsCopiedMapCells()
        {
            // HandleToggle's deactivate path calls ClearCopiedMapCells().
            // We test the field directly (without invoking HandleToggle, which
            // has full UI/overlay dependencies) by calling the private helper.
            var manager = NewManager();
            SetField(manager, "_copiedMapCells",
                new HashSet<Vector3Int> { new Vector3Int(3, 3, 0) });
            Assert.AreEqual(1, CopiedMapCells(manager).Count, "Pre-condition.");

            InvokePrivate(manager, "ClearCopiedMapCells");

            Assert.AreEqual(0, CopiedMapCells(manager).Count,
                "ClearCopiedMapCells must empty the set (called by deactivate branch).");
        }

        [Test]
        public void Picker_Deactivate_ClearsCopyHighlight()
        {
            // ClearTilesetCopyHighlight() is called by the manager's deactivate branch.
            // Verify it empties _tilesetCopiedSlots and deactivates all CopyHL overlays.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            Assert.AreEqual(1, CopiedSlots(ui).Count, "Pre-condition: one slot copied.");
            Assert.IsTrue(slots[0].CopyHL.activeSelf, "Pre-condition: CopyHL active.");

            ui.ClearTilesetCopyHighlight();

            Assert.AreEqual(0, CopiedSlots(ui).Count,
                "ClearTilesetCopyHighlight must empty _tilesetCopiedSlots.");
            Assert.IsFalse(slots[0].CopyHL.activeSelf,
                "ClearTilesetCopyHighlight must deactivate the CopyHL overlay.");
        }

        [Test]
        public void Map_NothingSelected_CopyDoesNotSnapshot()
        {
            // If the user presses Ctrl+C with an empty selection, OnCopyClicked
            // returns early without touching _copiedMapCells.
            var manager = NewManager();
            AttachWorldGrid(manager);

            // Ensure the set is already populated from a prior copy.
            SetField(manager, "_copiedMapCells",
                new HashSet<Vector3Int> { new Vector3Int(9, 9, 0) });

            manager.State.SelectedCells.Clear(); // nothing selected
            InvokePrivate(manager, "OnCopyClicked");

            // Set must be UNCHANGED because the copy was aborted.
            Assert.AreEqual(1, CopiedMapCells(manager).Count,
                "When SelectedCells is empty OnCopyClicked must bail before touching _copiedMapCells.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PICKER SIDE
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Picker_CommitTilesetSelection_SnapshotsCopiedSlots()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (0, 2));

            // Single-click slot[1] → CommitTilesetSelection fires automatically.
            SlotDown(ui, slots[1]);
            SlotUp(ui, slots[1]);

            var copied = CopiedSlots(ui);
            Assert.AreEqual(1, copied.Count,
                "CommitTilesetSelection must snapshot the selected slots into _tilesetCopiedSlots.");
            Assert.IsTrue(copied.Contains(slots[1].Pos));
        }

        [Test]
        public void Picker_SecondCommit_ReplacesCopiedSlots()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            Assert.AreEqual(1, CopiedSlots(ui).Count, "Pre-condition: one slot copied.");

            SlotDown(ui, slots[1]); SlotUp(ui, slots[1]);

            var copied = CopiedSlots(ui);
            Assert.AreEqual(1, copied.Count, "Second commit must REPLACE the first snapshot.");
            Assert.IsTrue(copied.Contains(slots[1].Pos), "Second-clicked slot must be present.");
            Assert.IsFalse(copied.Contains(slots[0].Pos), "First-clicked slot must be gone.");
        }

        [Test]
        public void Picker_ClearTilesetSelection_ClearsCopiedSlots()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            Assert.AreEqual(1, CopiedSlots(ui).Count, "Pre-condition.");

            ui.ClearTilesetSelection();

            Assert.AreEqual(0, CopiedSlots(ui).Count,
                "ClearTilesetSelection must empty _tilesetCopiedSlots.");
        }

        [Test]
        public void Picker_ResetPickerSelectionState_ClearsCopiedSlots()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            Assert.AreEqual(1, CopiedSlots(ui).Count, "Pre-condition.");

            InvokePrivate(ui, "ResetPickerSelectionState");

            Assert.AreEqual(0, CopiedSlots(ui).Count,
                "ResetPickerSelectionState (category change) must clear _tilesetCopiedSlots.");
        }

        [Test]
        public void Picker_CopyHL_ActivatesForCopiedSlot()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1), (0, 2));

            SlotDown(ui, slots[1]); SlotUp(ui, slots[1]);

            Assert.IsFalse(slots[0].CopyHL.activeSelf, "Non-copied slot must have CopyHL hidden.");
            Assert.IsTrue (slots[1].CopyHL.activeSelf, "Copied slot must have CopyHL active.");
            Assert.IsFalse(slots[2].CopyHL.activeSelf, "Non-copied slot must have CopyHL hidden.");
        }

        [Test]
        public void Picker_CopyHL_HiddenAfterClearTilesetSelection()
        {
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Multi);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            SlotDown(ui, slots[1]); SlotUp(ui, slots[1]);
            Assert.IsTrue(slots[0].CopyHL.activeSelf);
            Assert.IsTrue(slots[1].CopyHL.activeSelf);

            ui.ClearTilesetSelection();

            Assert.IsFalse(slots[0].CopyHL.activeSelf,
                "ClearTilesetSelection must deactivate all CopyHL overlays.");
            Assert.IsFalse(slots[1].CopyHL.activeSelf);
        }

        [Test]
        public void Picker_SelectHL_AndCopyHL_CoexistOnSameSlot()
        {
            // After picking slot 0 (which sets it as selected AND copied), both
            // the green DragHL and the yellow CopyHL must be active simultaneously.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0), (0, 1));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);

            Assert.IsTrue(slots[0].SelectHL.activeSelf, "DragHL (green) must be active.");
            Assert.IsTrue(slots[0].CopyHL.activeSelf,   "CopyHL (yellow) must also be active.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // INDEPENDENCE
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void Independence_PickerCommit_DoesNotClearMapCopiedCells()
        {
            // Copying on the map then selecting in the picker must not wipe the
            // map-side yellow outline — each surface tracks its own copy source.
            var manager = NewManager();
            AttachWorldGrid(manager);

            SetSelectedCells(manager, new Vector3Int(5, 5, 0));
            InvokePrivate(manager, "OnCopyClicked");
            Assert.AreEqual(1, CopiedMapCells(manager).Count, "Pre-condition: map copy done.");

            // Simulate picker commit by directly manipulating the picker-side state.
            // (We don't have a UI wired to the manager here — just verify the manager
            // field is untouched.)
            InvokePrivate(manager, "SnapshotCopiedMapCells",
                (IEnumerable<Vector3Int>)new List<Vector3Int> { new Vector3Int(5, 5, 0) });

            Assert.AreEqual(1, CopiedMapCells(manager).Count,
                "Map copied cells must survive a picker-side commit.");
        }

        [Test]
        public void Independence_MapCopy_DoesNotClearPickerCopiedSlots()
        {
            // Picking on the picker then doing a map Copy must not wipe the
            // picker-side yellow outline.
            var ui = CreateMinimalUI(TileEditorState.SelectMode.Single);
            var slots = RegisterSlots(ui, (0, 0));

            SlotDown(ui, slots[0]); SlotUp(ui, slots[0]);
            Assert.AreEqual(1, CopiedSlots(ui).Count, "Pre-condition: picker copy done.");

            // Simulate a map Copy by directly calling SnapshotCopiedMapCells on
            // a manager that does NOT share state with the UI above.
            var manager = NewManager();
            SetField(manager, "_copiedMapCells", new HashSet<Vector3Int>());
            InvokePrivate(manager, "SnapshotCopiedMapCells",
                (IEnumerable<Vector3Int>)new List<Vector3Int> { new Vector3Int(1, 1, 0) });

            // Picker's copied slots must be unaffected.
            Assert.AreEqual(1, CopiedSlots(ui).Count,
                "Picker _tilesetCopiedSlots must be unaffected by a map-side Copy.");
            Assert.IsTrue(CopiedSlots(ui).Contains(slots[0].Pos));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // GL OVERLAY API (TileEditorGridOverlay)
        // ═══════════════════════════════════════════════════════════════════════

        [Test]
        public void GridOverlay_SetCopiedCells_PopulatesInternalSet()
        {
            var go = new GameObject("Overlay");
            _sceneObjects.Add(go);
            var overlay = go.AddComponent<TileEditorGridOverlay>();

            overlay.SetCopiedCells(new List<Vector3Int>
            {
                new Vector3Int(2, 3, 0),
                new Vector3Int(4, 5, 0),
            });

            var set = GetField<HashSet<Vector2Int>>(overlay, "_copiedCells");
            Assert.AreEqual(2, set.Count, "SetCopiedCells must populate _copiedCells.");
            Assert.IsTrue(set.Contains(new Vector2Int(2, 3)));
            Assert.IsTrue(set.Contains(new Vector2Int(4, 5)));
        }

        [Test]
        public void GridOverlay_SetCopiedCells_Null_ClearsInternalSet()
        {
            var go = new GameObject("Overlay");
            _sceneObjects.Add(go);
            var overlay = go.AddComponent<TileEditorGridOverlay>();

            overlay.SetCopiedCells(new List<Vector3Int> { new Vector3Int(1, 1, 0) });
            overlay.SetCopiedCells(null);

            var set = GetField<HashSet<Vector2Int>>(overlay, "_copiedCells");
            Assert.AreEqual(0, set.Count, "SetCopiedCells(null) must clear the internal set.");
        }

        [Test]
        public void GridOverlay_SetCopiedCells_Replace_OnSecondCall()
        {
            var go = new GameObject("Overlay");
            _sceneObjects.Add(go);
            var overlay = go.AddComponent<TileEditorGridOverlay>();

            overlay.SetCopiedCells(new List<Vector3Int> { new Vector3Int(0, 0, 0) });
            overlay.SetCopiedCells(new List<Vector3Int>
            {
                new Vector3Int(7, 7, 0),
                new Vector3Int(8, 8, 0),
            });

            var set = GetField<HashSet<Vector2Int>>(overlay, "_copiedCells");
            Assert.AreEqual(2, set.Count, "Second SetCopiedCells call must REPLACE the first.");
            Assert.IsFalse(set.Contains(new Vector2Int(0, 0)), "First call's entry must be gone.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // PICKER ↔ MAP SELECTION EXCLUSIVITY
        //
        // Reported bug: a stale green selection on the MAP would persist after
        // a picker rect-select, so Ctrl+C (OnCopyClicked) read the stale map
        // cells and overwrote the freshly-populated picker clipboard. The fix
        // is that a multi-tile picker commit now wipes the map's pending
        // SelectedCells, and a successful map Copy/Cut wipes the picker's
        // green/yellow visuals — only one "active source" at a time.
        //
        // The clipboard itself is never touched by either side's cleanup —
        // each side preserves whatever its peer just wrote.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Combined helper: manager + UI sharing the SAME <see cref="TileEditorState"/>
        /// reference (production wiring) plus the requested picker slots already
        /// registered. Forces the singleton instance and wires <c>manager._ui</c>
        /// so both directions of the picker↔map cross-call resolve correctly:
        /// <list type="bullet">
        ///   <item>Picker → manager: <c>TileEditorManager.Instance</c> returns THIS manager.</item>
        ///   <item>Manager → picker: <c>_ui?.ClearPickerSelectionFromMapCopy()</c> hits THIS ui.</item>
        /// </list>
        /// Without the explicit singleton override, residual <c>_instance</c>
        /// state from a previous test (Singleton's Awake uses async Destroy
        /// for duplicates, so OnDestroy may not have run yet) would make this
        /// manager's Awake destroy its own GameObject as a duplicate.
        /// </summary>
        private (TileEditorManager manager, TileEditorUI ui, List<SlotHandle> slots)
            BuildManagerWithPicker(TileEditorState.SelectMode mode,
                params (int r, int c)[] coords)
        {
            // Pin the singleton to null BEFORE AddComponent so the new manager's
            // Awake takes the "first instance" branch and self-registers.
            SetSingletonInstance(null);

            var manager = NewManager();
            AttachWorldGrid(manager);
            // Belt-and-suspenders: explicitly force the static _instance to
            // this manager, in case a prior test left a zombie reference.
            SetSingletonInstance(manager);

            var state = manager.State;
            state.CurrentSelectMode = mode;
            state.CurrentTool = TileEditorState.Tool.Select;

            var uiGo = new GameObject("TileEditorUI_Exclusivity");
            uiGo.transform.SetParent(manager.transform);
            _sceneObjects.Add(uiGo);
            var ui = uiGo.AddComponent<TileEditorUI>();
            SetField(ui, "_state", state);

            // Wire the UI into the manager so OnCopyClicked / OnCutClicked
            // can reach _ui.ClearPickerSelectionFromMapCopy().
            SetField(manager, "_ui", ui);

            var slots = RegisterSlots(ui, coords);
            return (manager, ui, slots);
        }

        /// <summary>
        /// Force the <c>SingletonMonoBehaviour&lt;TileEditorManager&gt;._instance</c>
        /// static field to <paramref name="value"/>. Used to defeat test pollution
        /// where a previous fixture left a zombie reference.
        /// </summary>
        private static void SetSingletonInstance(TileEditorManager value)
        {
            // _instance lives on SingletonMonoBehaviour<TileEditorManager>,
            // which is TileEditorManager.BaseType.
            var t = typeof(TileEditorManager).BaseType;
            var f = t?.GetField("_instance",
                BindingFlags.Static | BindingFlags.NonPublic);
            f?.SetValue(null, value);
        }

        [Test]
        public void Picker_MultiTileRectCommit_ClearsMapSelectedCells()
        {
            // The headline regression: a stale map selection MUST be wiped when
            // the picker commits a multi-tile rect, so the next Ctrl+C doesn't
            // overwrite the picker clipboard with stale map cells.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Rect,
                (0, 0), (0, 1));

            manager.State.SelectedCells.Add(new Vector3Int(10, 10, 0));
            manager.State.SelectedCells.Add(new Vector3Int(10, 11, 0));
            manager.State.SelectedCellPos = new Vector3Int(10, 10, 0);

            SlotDown(ui, slots[0]);
            InvokePrivate(ui, "OnTilesetSlotEnter", slots[1].R, slots[1].C);
            SlotUp(ui, slots[1]);

            Assert.AreEqual(0, manager.State.SelectedCells.Count,
                "A multi-tile picker rect commit must clear the map's SelectedCells " +
                "so Ctrl+C doesn't shadow the picker clipboard.");
            Assert.IsFalse(manager.State.SelectedCellPos.HasValue,
                "Map SelectedCellPos must also be nulled by the picker commit.");
        }

        [Test]
        public void Picker_MultiTileRectCommit_ClearsMapCopiedMapCells()
        {
            // The yellow CopyHL outline drawn on the MAP must also disappear
            // when the picker becomes the new clipboard source — otherwise the
            // user sees two simultaneous "this is copied" indicators.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Rect,
                (0, 0), (0, 1));

            var copied = CopiedMapCells(manager);
            copied.Add(new Vector3Int(5, 5, 0));

            SlotDown(ui, slots[0]);
            InvokePrivate(ui, "OnTilesetSlotEnter", slots[1].R, slots[1].C);
            SlotUp(ui, slots[1]);

            Assert.AreEqual(0, CopiedMapCells(manager).Count,
                "A multi-tile picker commit must also clear the map's yellow CopyHL set.");
        }

        [Test]
        public void Picker_MultiTileRectCommit_PreservesClipboard()
        {
            // The bug-fix must NOT regress the clipboard the picker just wrote.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Rect,
                (0, 0), (0, 1));

            SlotDown(ui, slots[0]);
            InvokePrivate(ui, "OnTilesetSlotEnter", slots[1].R, slots[1].C);
            SlotUp(ui, slots[1]);

            Assert.IsNotNull(manager.State.Clipboard,
                "Picker rect commit must leave the clipboard populated.");
            Assert.AreEqual(2, manager.State.Clipboard.Width,
                "Clipboard width should reflect the picker rect.");
            Assert.AreEqual(1, manager.State.Clipboard.Height,
                "Clipboard height should reflect the picker rect.");
        }

        [Test]
        public void Picker_SingleClickCommit_KeepsMapSelectedCells()
        {
            // Single-click on the picker is the brush-pick gesture, not a copy.
            // Map selection must survive so the user can keep a Select-tool
            // region active while changing brush tiles.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Single,
                (0, 0), (0, 1));

            manager.State.SelectedCells.Add(new Vector3Int(7, 7, 0));
            manager.State.SelectedCellPos = new Vector3Int(7, 7, 0);

            SlotDown(ui, slots[0]);

            Assert.AreEqual(1, manager.State.SelectedCells.Count,
                "Single-tile picker click must NOT clear the map selection — " +
                "it's the typical brush-pick gesture and clearing would " +
                "disrupt brush-then-paint workflows.");
            Assert.IsTrue(manager.State.SelectedCellPos.HasValue,
                "Map SelectedCellPos must survive a single-tile picker click.");
        }

        [Test]
        public void Map_OnCopyClicked_ClearsPickerSelectedSlots()
        {
            // Symmetric to the picker→map clear: when the MAP becomes the
            // clipboard source, the picker's green selection becomes stale and
            // must be wiped so the user sees only one active source.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Multi,
                (0, 0), (0, 1));

            // Plant a picker selection (without touching map yet).
            SlotDown(ui, slots[0]);
            SlotUp(ui, slots[0]);
            SlotDown(ui, slots[1]);
            SlotUp(ui, slots[1]);
            var pickerSel = GetField<HashSet<Vector2Int>>(ui, "_tilesetSelectedSlots");
            Assert.AreEqual(2, pickerSel.Count, "Pre-condition: picker has 2 slots selected.");

            // Now stage a map selection and trigger Ctrl+C.
            SetSelectedCells(manager, new Vector3Int(20, 20, 0));
            InvokePrivate(manager, "OnCopyClicked");

            Assert.AreEqual(0, pickerSel.Count,
                "After map OnCopyClicked, the picker's green selection must be cleared.");
            Assert.AreEqual(0, CopiedSlots(ui).Count,
                "After map OnCopyClicked, the picker's yellow CopyHL set must also be cleared.");
        }

        [Test]
        public void Map_OnCopyClicked_PreservesClipboardWithMapTiles()
        {
            // Counterpart guarantee: the clipboard now reflects the MAP source
            // after Ctrl+C, even though the picker's selection was wiped.
            var (manager, ui, slots) = BuildManagerWithPicker(
                TileEditorState.SelectMode.Single,
                (0, 0));

            var tilemap = manager.GetType()
                .GetMethod("GetCurrentTilemap",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(manager, null) as Tilemap;

            var mapTile = ScriptableObject.CreateInstance<Tile>();
            _assets.Add(mapTile);
            tilemap.SetTile(new Vector3Int(30, 30, 0), mapTile);

            SetSelectedCells(manager, new Vector3Int(30, 30, 0));
            InvokePrivate(manager, "OnCopyClicked");

            Assert.IsNotNull(manager.State.Clipboard,
                "Map OnCopyClicked must populate the clipboard.");
            Assert.AreSame(mapTile, manager.State.Clipboard.Tiles[0, 0],
                "Clipboard must contain the MAP tile (not stale picker data).");
        }
    }
}
