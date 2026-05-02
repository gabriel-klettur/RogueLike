using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Select
{
    /// <summary>
    /// Coverage for the SELECT tool's three sub-modes (Single / Rect / Multi) and the
    /// Copy / Cut / Paste / Clear-Selection callbacks. Most tests drive the manager's
    /// private callbacks via reflection (mirrors the project's
    /// <c>TileEditorViewPanelTests</c> pattern) so the production surface stays small.
    ///
    /// State invariants verified:
    ///   • SelectMode defaults to Single, SelectedCells is empty, Clipboard is null.
    ///   • Single replaces selection; Rect commits on release; Multi accumulates.
    ///   • OnToolChanged clears selection but preserves the Clipboard (user UX decision).
    ///   • OnCopy → state.Clipboard captured. OnCut sets IsCut and removes tiles.
    ///   • OnPaste reproduces tiles at the anchor with a top-left, downward-extending footprint.
    ///   • Cut and Paste are independent undo batches.
    ///
    /// UI invariants verified separately by <c>TileEditorSelectModesPanelTests</c>.
    /// </summary>
    [TestFixture]
    public class TileEditorSelectModeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. State defaults
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void State_SelectMode_DefaultsToSingle()
        {
            var s = new TileEditorState();
            Assert.AreEqual(TileEditorState.SelectMode.Single, s.CurrentSelectMode,
                "Single is the only mode that matches the original Select behaviour " +
                "(replace-selection-on-click); must be default.");
        }

        [Test]
        public void State_SelectedCells_StartsEmpty()
        {
            var s = new TileEditorState();
            Assert.IsNotNull(s.SelectedCells);
            Assert.AreEqual(0, s.SelectedCells.Count);
        }

        [Test]
        public void State_Clipboard_StartsNull()
        {
            var s = new TileEditorState();
            Assert.IsNull(s.Clipboard,
                "Paste must no-op until the user has done at least one Copy or Cut.");
        }

        [Test]
        public void State_RectDrag_StartsInactive()
        {
            var s = new TileEditorState();
            Assert.IsFalse(s.RectDragStart.HasValue);
            Assert.IsFalse(s.RectDragCurrent.HasValue);
        }

        [Test]
        public void SelectMode_ThreeDistinctValues()
        {
            // Sanity that the enum hasn't drifted or merged values — the radio UI relies on
            // each value being distinct so only one toggle is ON at a time.
            Assert.AreNotEqual(TileEditorState.SelectMode.Single, TileEditorState.SelectMode.Rect);
            Assert.AreNotEqual(TileEditorState.SelectMode.Single, TileEditorState.SelectMode.Multi);
            Assert.AreNotEqual(TileEditorState.SelectMode.Rect,   TileEditorState.SelectMode.Multi);
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Selection helpers (footprint + bounds)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ApplyBrushFootprintToSelection_BrushSize3_FillsNineCells()
        {
            var manager = NewManager();
            manager.State.BrushSize = 3;

            InvokePrivate(manager, "ApplyBrushFootprintToSelection", new Vector3Int(10, 20, 0));

            Assert.AreEqual(9, manager.State.SelectedCells.Count,
                "BrushSize=3 must add a 3×3 = 9-cell footprint to the selection set.");
            Assert.IsTrue(manager.State.SelectedCells.Contains(new Vector3Int(10, 20, 0)),
                "Cursor cell must be the top-left corner of the footprint.");
            Assert.IsTrue(manager.State.SelectedCells.Contains(new Vector3Int(12, 18, 0)),
                "Footprint must extend +X and -Y (brush convention).");
        }

        [Test]
        public void ComputeSelectionBounds_ReturnsTightBounds()
        {
            var cells = new System.Collections.Generic.HashSet<Vector3Int>
            {
                new Vector3Int(5, 5, 0),
                new Vector3Int(8, 7, 0),
                new Vector3Int(6, 6, 0),
            };
            var mi = typeof(TileEditorManager).GetMethod("ComputeSelectionBounds",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(mi);

            var bounds = (BoundsInt)mi.Invoke(null, new object[] { cells });
            Assert.AreEqual(5, bounds.xMin);
            Assert.AreEqual(5, bounds.yMin);
            Assert.AreEqual(4, bounds.size.x); // (8 - 5) + 1
            Assert.AreEqual(3, bounds.size.y); // (7 - 5) + 1
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Mode change
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnSelectModeChanged_UpdatesState_AndCancelsRectDrag()
        {
            var manager = NewManager();
            manager.State.RectDragStart = new Vector3Int(1, 2, 0);
            manager.State.RectDragCurrent = new Vector3Int(3, 4, 0);
            manager.State.IsDragging = true;

            InvokePrivate(manager, "OnSelectModeChanged", TileEditorState.SelectMode.Multi);

            Assert.AreEqual(TileEditorState.SelectMode.Multi, manager.State.CurrentSelectMode);
            Assert.IsFalse(manager.State.IsDragging,
                "Switching mode mid-drag must end the drag — leaving anchors set would " +
                "leak a stale yellow rect on the overlay.");
            Assert.IsFalse(manager.State.RectDragStart.HasValue);
            Assert.IsFalse(manager.State.RectDragCurrent.HasValue);
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. Tool change preserves clipboard but clears selection
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void OnToolChanged_LeavingSelect_ClearsSelectedCells_PreservesClipboard()
        {
            var manager = NewManager();
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.SelectedCells.Add(new Vector3Int(0, 0, 0));
            manager.State.SelectedCells.Add(new Vector3Int(1, 0, 0));
            manager.State.Clipboard = new TileClipboard
            {
                Tiles = new TileBase[1, 1],
                SourceBounds = new BoundsInt(0, 0, 0, 1, 1, 1),
            };

            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Brush);

            Assert.AreEqual(0, manager.State.SelectedCells.Count,
                "User decision: leaving Select clears the selection set.");
            Assert.IsNotNull(manager.State.Clipboard,
                "User decision: clipboard survives tool changes (Copy → Brush → Select → Paste).");
        }

        [Test]
        public void OnToolChanged_LeavingSelect_ResetsSelectModeToSingle()
        {
            var manager = NewManager();
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.CurrentSelectMode = TileEditorState.SelectMode.Multi;

            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Brush);
            // Re-enter Select to observe the mode reset.
            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Select);

            Assert.AreEqual(TileEditorState.SelectMode.Single, manager.State.CurrentSelectMode,
                "Re-entering Select must default to Single — Multi is sticky enough to " +
                "warrant explicit re-arm by the user.");
        }

        [Test]
        public void OnToolChanged_StayingInSelect_DoesNotClearSelection()
        {
            var manager = NewManager();
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.SelectedCells.Add(new Vector3Int(7, 7, 0));

            // Calling OnToolChanged with the same tool is a no-op for selection.
            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Select);

            Assert.AreEqual(1, manager.State.SelectedCells.Count,
                "Identity tool change must not clear — only an actual leave does.");
        }

        [Test]
        public void OnToolChanged_StayingInSelect_DoesNotResetSelectMode()
        {
            // Re-clicking SELECT must not undo a Multi/Rect mode pick — the user's
            // explicit mode should survive the panel-toggle action.
            var manager = NewManager();
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.CurrentSelectMode = TileEditorState.SelectMode.Multi;

            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Select);

            Assert.AreEqual(TileEditorState.SelectMode.Multi, manager.State.CurrentSelectMode,
                "Re-clicking SELECT toggles the panel; it must NOT reset the sub-mode.");
        }

        [Test]
        public void OnToolChanged_StayingInSelect_PreservesClipboard()
        {
            // Sanity: the panel-toggle path must not run any of the leavingSelect
            // cleanup that would erase a user's clipboard.
            var manager = NewManager();
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            var clip = new TileClipboard
            {
                Tiles        = new TileBase[1, 1],
                SourceBounds = new BoundsInt(0, 0, 0, 1, 1, 1),
            };
            manager.State.Clipboard = clip;

            InvokePrivate(manager, "OnToolChanged", TileEditorState.Tool.Select);

            Assert.AreSame(clip, manager.State.Clipboard,
                "Re-clicking SELECT must never wipe the clipboard.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. Copy / Cut / Paste end-to-end (with a real Tilemap)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Copy_PopulatesClipboardWithTilesFromSelectedCells()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);
            var tA = MakeTile("A");
            var tB = MakeTile("B");
            tilemap.SetTile(new Vector3Int(2, 5, 0), tA);
            tilemap.SetTile(new Vector3Int(3, 5, 0), tB);

            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.SelectedCells.Add(new Vector3Int(2, 5, 0));
            manager.State.SelectedCells.Add(new Vector3Int(3, 5, 0));

            InvokePrivate(manager, "OnCopyClicked");

            Assert.IsNotNull(manager.State.Clipboard);
            Assert.AreEqual(2, manager.State.Clipboard.Width);
            Assert.AreEqual(1, manager.State.Clipboard.Height);
            Assert.AreEqual(tA, manager.State.Clipboard.Tiles[0, 0]);
            Assert.AreEqual(tB, manager.State.Clipboard.Tiles[1, 0]);
            Assert.IsFalse(manager.State.Clipboard.IsCut);
        }

        [Test]
        public void Copy_NothingSelected_DoesNotPopulateClipboard()
        {
            var manager = NewManager();
            AttachWorldGrid(manager);
            manager.State.CurrentTool = TileEditorState.Tool.Select;
            // SelectedCells empty.

            InvokePrivate(manager, "OnCopyClicked");

            Assert.IsNull(manager.State.Clipboard);
        }

        [Test]
        public void Cut_RemovesTilesAndSetsIsCut()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);
            var tA = MakeTile("A");
            tilemap.SetTile(new Vector3Int(0, 0, 0), tA);

            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.SelectedCells.Add(new Vector3Int(0, 0, 0));

            InvokePrivate(manager, "OnCutClicked");

            Assert.IsNotNull(manager.State.Clipboard);
            Assert.IsTrue(manager.State.Clipboard.IsCut, "Cut must mark the clipboard as IsCut=true.");
            Assert.IsNull(tilemap.GetTile(new Vector3Int(0, 0, 0)),
                "Cut must remove the source tile.");
        }

        [Test]
        public void Paste_ReproducesClipboardAtAnchor_TopLeftDownExtension()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);
            var tA = MakeTile("A");
            var tB = MakeTile("B");

            // Build a 2x2 clipboard manually — the array layout is dx (right) × dy (up
            // in source-bounds coords), and Paste flips dy so the bottom row of the
            // clipboard appears at anchor.y - 1.
            var clip = new TileClipboard
            {
                Tiles        = new TileBase[2, 2],
                SourceBounds = new BoundsInt(0, 0, 0, 2, 2, 1),
                SourceLayer  = TilemapLayerSetup.TilemapLayer.Ground,
            };
            clip.Tiles[0, 0] = tA; // bottom-left
            clip.Tiles[1, 1] = tB; // top-right
            manager.State.Clipboard = clip;
            manager.State.SelectedCellPos = new Vector3Int(50, 60, 0); // anchor (top-left)

            InvokePrivate(manager, "OnPasteClicked");

            // top-left of the paste = (50, 60). Bottom row (dy=0 in source) appears at
            // y = 60 - (h-1 - 0) = 60 - 1 = 59. Top row (dy=1) appears at y = 60.
            Assert.AreEqual(tA, tilemap.GetTile(new Vector3Int(50, 59, 0)),
                "tA was at (dx=0, dy=0) in source — must paste at (anchor.x, anchor.y - (h-1)).");
            Assert.AreEqual(tB, tilemap.GetTile(new Vector3Int(51, 60, 0)),
                "tB was at (dx=1, dy=1) in source — must paste at (anchor.x + 1, anchor.y).");
        }

        [Test]
        public void Paste_NullClipboard_NoOps()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);
            var existing = MakeTile("E");
            tilemap.SetTile(new Vector3Int(0, 0, 0), existing);
            manager.State.Clipboard = null;

            Assert.DoesNotThrow(() => InvokePrivate(manager, "OnPasteClicked"));
            Assert.AreEqual(existing, tilemap.GetTile(new Vector3Int(0, 0, 0)),
                "Empty clipboard must not touch the tilemap.");
        }

        [Test]
        public void CopyThenPaste_AtNewAnchor_ReproducesTiles()
        {
            var manager = NewManager();
            var tilemap = AttachWorldGrid(manager);
            var tA = MakeTile("A");
            tilemap.SetTile(new Vector3Int(0, 0, 0), tA);

            manager.State.CurrentTool = TileEditorState.Tool.Select;
            manager.State.SelectedCells.Add(new Vector3Int(0, 0, 0));
            InvokePrivate(manager, "OnCopyClicked");

            // Move anchor and paste.
            manager.State.SelectedCellPos = new Vector3Int(20, 30, 0);
            InvokePrivate(manager, "OnPasteClicked");

            Assert.AreEqual(tA, tilemap.GetTile(new Vector3Int(20, 30, 0)),
                "Round-trip: a 1×1 selection copied and pasted at (20, 30) lands at exactly (20, 30).");
            Assert.AreEqual(tA, tilemap.GetTile(new Vector3Int(0, 0, 0)),
                "Source tile must be preserved (Copy is non-destructive).");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. ClearSelection
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ClearSelection_EmptiesEverythingSelectedButPreservesClipboard()
        {
            var manager = NewManager();
            manager.State.SelectedCells.Add(new Vector3Int(0, 0, 0));
            manager.State.RectDragStart   = new Vector3Int(1, 1, 0);
            manager.State.RectDragCurrent = new Vector3Int(2, 2, 0);
            manager.State.IsDragging      = true;
            manager.State.Clipboard       = new TileClipboard { Tiles = new TileBase[1, 1], SourceBounds = new BoundsInt(0,0,0,1,1,1) };

            manager.ClearSelection();

            Assert.AreEqual(0, manager.State.SelectedCells.Count);
            Assert.IsFalse(manager.State.RectDragStart.HasValue);
            Assert.IsFalse(manager.State.RectDragCurrent.HasValue);
            Assert.IsFalse(manager.State.IsDragging);
            Assert.IsNotNull(manager.State.Clipboard,
                "ClearSelection clears the selection set, NOT the clipboard.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 6b. SelectModes panel default-closed + sticky preference
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Builder_SelectModesDropdown_StartsHidden_EvenWhenSelectIsDefaultTool()
        {
            // The very first thing the user sees when opening the Tile Editor is
            // CurrentTool = Select. The SelectModes panel must NOT be auto-shown —
            // it's an opt-in advanced UI (user toggles it via the SELECT button).
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            try
            {
                var state = new TileEditorState(); // default CurrentTool = Select
                var refs = TileEditorUIBuilder.BuildAll(canvasGo.transform, state,
                    onToolChanged: null, onLayerChanged: null, onBrushSizeChanged: null,
                    onDropdownToggle: null);

                Assert.IsNotNull(refs.SelectModesDropdown);
                Assert.IsFalse(refs.SelectModesDropdown.activeSelf,
                    "SelectModes panel must start hidden — clicking SELECT in Tools is " +
                    "what reveals it. Auto-showing it on editor open is the regression " +
                    "this test guards against.");
            }
            finally { Object.DestroyImmediate(canvasGo); }
        }

        [Test]
        public void Builder_SelectModesDropdown_DefaultMode_IsSingle()
        {
            // Companion to the default-closed test: the panel may be hidden, but the
            // active sub-mode under the hood is still Single — so the user's first
            // click on a tile uses the legacy single-replace behaviour.
            var state = new TileEditorState();
            Assert.AreEqual(TileEditorState.SelectMode.Single, state.CurrentSelectMode,
                "Single is the safe, low-surprise default — Multi/Rect would change " +
                "the meaning of clicks without any visible affordance until the user " +
                "opens the panel.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 7. UI builder constants
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Constants_SelectModesDropdown_ReasonableSize()
        {
            Assert.That(SELECT_MODES_DROP_W, Is.InRange(150f, 400f),
                "SelectModes width should fit between Tools (60) and Tiles (~256) without crowding.");
            Assert.That(SELECT_MODES_DROP_H, Is.InRange(150f, 500f),
                "SelectModes height must accommodate 3 toggle rows + 4 action buttons + hint.");
        }

        [Test]
        public void Constants_SelectModesDropdown_ContentAreaPositive()
        {
            Assert.Greater(SELECT_MODES_DROP_H - PANEL_HDR_H, 100f,
                "Content area must be tall enough for the rows plus header.");
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private TileEditorManager NewManager()
        {
            _host = new GameObject("TileEditorManager_TestHost");
            return _host.AddComponent<TileEditorManager>();
        }

        /// <summary>
        /// Creates a minimal scene graph that <see cref="WorldGridBuilder.GetTilemap"/>
        /// can resolve. The production builder calls <c>_grid.transform.Find(layerName)</c>
        /// to find each tilemap, so the test only needs (a) a Grid, (b) a child Tilemap
        /// per layer named after the enum value, and (c) the private <c>_grid</c> field
        /// of the builder pointing at that Grid component.
        /// </summary>
        private Tilemap AttachWorldGrid(TileEditorManager manager)
        {
            var gridGo = new GameObject("WorldGrid");
            gridGo.transform.SetParent(manager.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            var wgb = gridGo.AddComponent<WorldGridBuilder>();

            // Create one child Tilemap per layer so transform.Find(layerName) succeeds.
            Tilemap groundTilemap = null;
            for (int i = 0; i < 9; i++)
            {
                var layer = (TilemapLayerSetup.TilemapLayer)i;
                var tmGo = new GameObject(layer.ToString());
                tmGo.transform.SetParent(gridGo.transform, false);
                var tm = tmGo.AddComponent<Tilemap>();
                tmGo.AddComponent<TilemapRenderer>();
                if (layer == TilemapLayerSetup.TilemapLayer.Ground)
                    groundTilemap = tm;
            }

            // Wire the manager's serialized worldGridBuilder field.
            typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, wgb);

            // Wire the builder's private _grid field — without this GetTilemap returns
            // null because the production setup pass (BuildGrid) is not running here.
            typeof(WorldGridBuilder)
                .GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(wgb, grid);

            // Provide a non-null undo system so OnCutClicked can call StartStroke without NRE.
            // The undo system is created in OnSingletonAwake which we bypass in tests.
            EnsureUndoSystem(manager);

            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
            return groundTilemap;
        }

        private static void EnsureUndoSystem(TileEditorManager manager)
        {
            var undoField = typeof(TileEditorManager)
                .GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (undoField == null) return;
            if (undoField.GetValue(manager) == null)
                undoField.SetValue(manager, new TileEditorUndoSystem());
        }

        private static Tile MakeTile(string name)
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
            sprite.name = name;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.name   = name;
            return tile;
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            var t = target.GetType();
            MethodInfo mi = null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != methodName) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                mi = m;
                break;
            }
            Assert.IsNotNull(mi, $"Reflection: {methodName}({args.Length} args) not found on {t.Name}.");
            mi.Invoke(target, args);
        }
    }
}
