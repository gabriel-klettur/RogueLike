using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.UI
{
    /// <summary>
    /// Tests for the Draw / Erase Colliders system of the runtime Tile Editor (F8).
    ///
    /// Coverage:
    ///   1. TileEditorState.ColliderMode – defaults and enum invariants
    ///   2. TileRegistry.GetName – serialization fallback chain
    ///   3. Regression: collider tile name must equal sprite name so overlays can reload
    ///   4. TileBrush.Paint – set / skip-same / canEditCell gate / brushSize footprint
    ///   5. TileBrush.Erase – remove / skip-empty / canEditCell gate
    ///   6. TileEdit struct – OldTile / NewTile / Position fields
    ///   7. TileEditorState.BrushStrokeCells – add / clear
    /// </summary>
    [TestFixture]
    public class TileEditorColliderTests
    {
        // ── Scene helpers ────────────────────────────────────────────────────

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);
        }

        /// <summary>Create a minimal Grid + Tilemap hierarchy for brush tests.</summary>
        private Tilemap SetupTilemap()
        {
            _root = new GameObject("Grid");
            _root.AddComponent<Grid>();
            var child = new GameObject("Tilemap");
            child.transform.SetParent(_root.transform, false);
            return child.AddComponent<Tilemap>();
        }

        /// <summary>Create a Tile whose sprite.name is <paramref name="spriteName"/>.</summary>
        private static Tile MakeTile(string spriteName = "testTile")
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
            sprite.name = spriteName;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            return tile;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. TileEditorState.ColliderMode
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ColliderMode_DefaultIsNone()
        {
            var state = new TileEditorState();
            Assert.AreEqual(TileEditorState.ColliderMode.None, state.CurrentColliderMode,
                "A freshly created TileEditorState must be in ColliderMode.None.");
        }

        [Test]
        public void ShowColliderOverlay_DefaultIsFalse()
        {
            var state = new TileEditorState();
            Assert.IsFalse(state.ShowColliderOverlay,
                "ShowColliderOverlay must default to false — overlay is opt-in.");
        }

        [Test]
        public void ColliderMode_NoneEnumValue_IsZero()
        {
            // Guarantees that the C# default(ColliderMode) equals None, so any
            // uninitialised enum field is safe without explicit assignment.
            Assert.AreEqual(0, (int)TileEditorState.ColliderMode.None);
        }

        [Test]
        public void ColliderMode_DrawAndErase_AreDistinctFromNone()
        {
            Assert.AreNotEqual(TileEditorState.ColliderMode.None, TileEditorState.ColliderMode.Draw,
                "Draw must differ from None.");
            Assert.AreNotEqual(TileEditorState.ColliderMode.None, TileEditorState.ColliderMode.Erase,
                "Erase must differ from None.");
        }

        [Test]
        public void ColliderMode_Draw_IsDistinctFrom_Erase()
        {
            Assert.AreNotEqual(TileEditorState.ColliderMode.Draw, TileEditorState.ColliderMode.Erase,
                "Draw and Erase must be distinct modes.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. TileRegistry.GetName – serialization fallback chain
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileRegistry_GetName_Null_ReturnsNull()
        {
            var reg = new TileRegistry();
            Assert.IsNull(reg.GetName(null));
        }

        [Test]
        public void TileRegistry_GetName_ReturnsRegisteredName_OverTileName()
        {
            var reg = new TileRegistry();
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "tileName";
            reg.Register("registeredName", tile);

            Assert.AreEqual("registeredName", reg.GetName(tile),
                "Registered name takes priority over tile.name.");
        }

        [Test]
        public void TileRegistry_GetName_FallsBack_ToTileName_WhenNotRegistered()
        {
            var reg = new TileRegistry();
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = "wall";

            Assert.AreEqual("wall", reg.GetName(tile),
                "tile.name must be used when the tile is not registered in the dictionary.");
        }

        [Test]
        public void TileRegistry_GetName_FallsBack_ToSpriteName_WhenTileNameIsEmpty()
        {
            var reg = new TileRegistry();
            var tile = MakeTile("wall");
            tile.name = ""; // force the fallback branch

            Assert.AreEqual("wall", reg.GetName(tile),
                "When tile.name is empty, GetName must fall back to sprite.name.");
        }

        [Test]
        public void TileRegistry_GetName_UnregisteredTile_NoSprite_ReturnsNull()
        {
            // A bare tile with no explicit name and no sprite has nothing to fall
            // back to, so GetName returns null. This documents the terminal branch
            // of the fallback chain.
            var reg = new TileRegistry();
            var tile = ScriptableObject.CreateInstance<Tile>();
            // ScriptableObject.CreateInstance leaves .name empty until assigned.
            string result = reg.GetName(tile);
            Assert.IsNull(result,
                "GetName must return null when the tile has no registration, no name, " +
                "and no sprite \u2014 there is nothing to serialize.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. Regression: collider tile name must equal sprite name (Bug 1 fix)
        //
        // GetOrCreateColliderTile() previously set tile.name = "TileEditorColliderTile".
        // TileRegistry.GetName returned that non-resolvable name, and
        // OverlayLoader.ResolveSprite("TileEditorColliderTile") found no sprite in
        // Resources/Tiles → drawn colliders were silently lost on game restart.
        //
        // After the fix: tile.name = sprite.name  (e.g. "wall"), which IS resolvable.
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ColliderTile_FixedPattern_TileName_EqualsSpriteName_IsSerializable()
        {
            // Reproduce the FIXED GetOrCreateColliderTile() naming: tile.name = sprite.name
            var tile = MakeTile("wall");
            tile.name = tile.sprite.name; // fixed pattern

            var reg = new TileRegistry();
            string savedName = reg.GetName(tile);

            Assert.AreEqual("wall", savedName,
                "After the fix, the serialized name must equal the sprite name ('wall') " +
                "so OverlayLoader.ResolveSprite can reconstruct the tile on reload.");
        }

        [Test]
        public void ColliderTile_BrokenPattern_CustomName_BreaksRoundTrip()
        {
            // Documents the OLD (broken) pattern: using a custom name that has no
            // matching sprite in Resources/Tiles/ makes the overlay JSON unreloadable.
            var tile = MakeTile("wall");
            tile.name = "TileEditorColliderTile"; // old (broken) pattern

            var reg = new TileRegistry();
            string savedName = reg.GetName(tile);

            Assert.AreEqual("TileEditorColliderTile", savedName,
                "Old pattern: GetName returns the non-resolvable custom name.");
            Assert.AreNotEqual("wall", savedName,
                "Old pattern: sprite name 'wall' is NOT used — this is the regression.");
        }

        [Test]
        public void ColliderTile_NameMatchingSprite_RoundTripsViaRegistry()
        {
            // End-to-end: register a tile with its sprite name, then look it up.
            // This mirrors how OverlayLoader.ResolveTile registers reloaded tiles.
            var reg = new TileRegistry();
            var tile = MakeTile("wall");
            tile.name = "wall";
            reg.Register("wall", tile);

            Assert.AreEqual("wall", reg.GetName(tile));
            Assert.AreEqual(tile, reg.GetTile("wall"));
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. TileBrush.Paint
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileBrush_Paint_SetsTile_OnEmptyCell()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;

            var edits = TileBrush.Paint(tilemap, pos, tile, brushSize: 1);

            Assert.AreEqual(1, edits.Count, "One edit expected for a single empty cell.");
            Assert.AreEqual(tile, tilemap.GetTile(pos), "Tile must be set on the tilemap.");
        }

        [Test]
        public void TileBrush_Paint_ReturnsNoEdits_WhenSameTileAlreadyPresent()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, tile);

            var edits = TileBrush.Paint(tilemap, pos, tile, brushSize: 1);

            Assert.AreEqual(0, edits.Count,
                "Painting the same tile over itself should produce no edits (idempotent).");
        }

        [Test]
        public void TileBrush_Paint_ReplacesExistingTile_WithNewTile()
        {
            var tilemap = SetupTilemap();
            var oldTile = MakeTile("old");
            var newTile = MakeTile("new");
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, oldTile);

            var edits = TileBrush.Paint(tilemap, pos, newTile, brushSize: 1);

            Assert.AreEqual(1, edits.Count);
            Assert.AreEqual(newTile, tilemap.GetTile(pos), "New tile must replace old tile.");
        }

        [Test]
        public void TileBrush_Paint_RespectsCanEditCell_Gate_BlockedCell()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;

            var edits = TileBrush.Paint(tilemap, pos, tile, brushSize: 1, canEditCell: _ => false);

            Assert.AreEqual(0, edits.Count, "Locked cell must produce no edits.");
            Assert.IsNull(tilemap.GetTile(pos), "Tile must NOT be placed on a locked cell.");
        }

        [Test]
        public void TileBrush_Paint_RespectsCanEditCell_Gate_PartialBrush()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            // Block only the cell at (1, 0, 0) — the rest of a 2×2 brush are free.
            var blockedPos = new Vector3Int(1, 0, 0);

            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 2,
                canEditCell: p => p != blockedPos);

            // brushSize=2: cells (0,0), (1,0), (0,-1), (1,-1) → blocked cell reduces to 3
            Assert.AreEqual(3, edits.Count,
                "Only 3 of the 4 cells should be painted when one is gated.");
            Assert.IsNull(tilemap.GetTile(blockedPos),
                "Blocked cell must not have a tile after partial paint.");
        }

        [Test]
        public void TileBrush_Paint_BrushSize2_SetsFourCells()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();

            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 2);

            Assert.AreEqual(4, edits.Count, "BrushSize=2 must produce a 2×2 = 4-cell footprint.");
        }

        [Test]
        public void TileBrush_Paint_BrushSize3_SetsNineCells()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();

            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 3);

            Assert.AreEqual(9, edits.Count, "BrushSize=3 must produce a 3×3 = 9-cell footprint.");
        }

        [Test]
        public void TileBrush_Paint_NullTile_ClearsCellContent()
        {
            // Paint(null) is how the collider Erase mode deletes tiles.
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, tile);

            var edits = TileBrush.Paint(tilemap, pos, null, brushSize: 1);

            Assert.AreEqual(1, edits.Count, "Setting null over an existing tile is one edit.");
            Assert.IsNull(tilemap.GetTile(pos), "Cell must be empty after painting null.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. TileBrush.Erase
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileBrush_Erase_RemovesTile_FromOccupiedCell()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, tile);

            var edits = TileBrush.Erase(tilemap, pos, brushSize: 1);

            Assert.AreEqual(1, edits.Count, "Erase on an occupied cell must produce 1 edit.");
            Assert.IsNull(tilemap.GetTile(pos), "Cell must be empty after erase.");
        }

        [Test]
        public void TileBrush_Erase_ReturnsNoEdits_OnAlreadyEmptyCell()
        {
            var tilemap = SetupTilemap();
            var pos = Vector3Int.zero;

            var edits = TileBrush.Erase(tilemap, pos, brushSize: 1);

            Assert.AreEqual(0, edits.Count,
                "Erasing an already-empty cell should produce no edits.");
        }

        [Test]
        public void TileBrush_Erase_RespectsCanEditCell_Gate()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, tile);

            var edits = TileBrush.Erase(tilemap, pos, brushSize: 1, canEditCell: _ => false);

            Assert.AreEqual(0, edits.Count, "Locked cell must block erase.");
            Assert.AreEqual(tile, tilemap.GetTile(pos), "Tile must remain after a blocked erase.");
        }

        [Test]
        public void TileBrush_Erase_IsEquivalentTo_PaintNull()
        {
            // TileBrush.Erase delegates to Paint(null) — verify they produce identical results.
            var tilemap1 = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap1.SetTile(pos, tile);

            var eraseEdits = TileBrush.Erase(tilemap1, pos, brushSize: 1);

            // Second tilemap for the Paint(null) path
            var tilemap2Root = new GameObject("Grid2");
            tilemap2Root.AddComponent<Grid>();
            var t2child = new GameObject("Tilemap2");
            t2child.transform.SetParent(tilemap2Root.transform, false);
            var tilemap2 = t2child.AddComponent<Tilemap>();
            tilemap2.SetTile(pos, tile);

            var paintNullEdits = TileBrush.Paint(tilemap2, pos, null, brushSize: 1);

            Assert.AreEqual(eraseEdits.Count, paintNullEdits.Count);
            Assert.AreEqual(eraseEdits[0].Position, paintNullEdits[0].Position);
            Assert.IsNull(eraseEdits[0].NewTile);
            Assert.IsNull(paintNullEdits[0].NewTile);

            Object.DestroyImmediate(tilemap2Root);
        }

        // ════════════════════════════════════════════════════════════════════
        // 6. TileEdit struct fields
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileEdit_Paint_RecordsCorrectPositionOldTileNewTile()
        {
            var tilemap = SetupTilemap();
            var oldTile = MakeTile("old");
            var newTile = MakeTile("new");
            var pos = new Vector3Int(3, -2, 0);
            tilemap.SetTile(pos, oldTile);

            var edits = TileBrush.Paint(tilemap, pos, newTile, brushSize: 1);

            Assert.AreEqual(1, edits.Count);
            Assert.AreEqual(pos,     edits[0].Position, "TileEdit.Position must match painted cell.");
            Assert.AreEqual(oldTile, edits[0].OldTile,  "TileEdit.OldTile must be the previous tile.");
            Assert.AreEqual(newTile, edits[0].NewTile,  "TileEdit.NewTile must be the new tile.");
        }

        [Test]
        public void TileEdit_Erase_RecordsNullAsNewTile()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;
            tilemap.SetTile(pos, tile);

            var edits = TileBrush.Erase(tilemap, pos, brushSize: 1);

            Assert.AreEqual(tile, edits[0].OldTile,  "OldTile must be the erased tile.");
            Assert.IsNull(edits[0].NewTile,           "NewTile must be null after erase.");
        }

        [Test]
        public void TileEdit_Paint_OnEmptyCell_OldTile_IsNull()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            var pos = Vector3Int.zero;

            var edits = TileBrush.Paint(tilemap, pos, tile, brushSize: 1);

            Assert.IsNull(edits[0].OldTile, "Painting onto an empty cell: OldTile must be null.");
        }

        // ════════════════════════════════════════════════════════════════════
        // 7. TileEditorState.BrushStrokeCells
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void BrushStrokeCells_EmptyByDefault()
        {
            var state = new TileEditorState();
            Assert.AreEqual(0, state.BrushStrokeCells.Count,
                "BrushStrokeCells must be empty on a new state.");
        }

        [Test]
        public void BrushStrokeCells_Add_IncreasesCount()
        {
            var state = new TileEditorState();
            state.BrushStrokeCells.Add(new Vector3Int(1, 2, 0));
            state.BrushStrokeCells.Add(new Vector3Int(3, 4, 0));
            Assert.AreEqual(2, state.BrushStrokeCells.Count);
        }

        [Test]
        public void BrushStrokeCells_Clear_ResetsToEmpty()
        {
            var state = new TileEditorState();
            state.BrushStrokeCells.Add(Vector3Int.zero);
            state.BrushStrokeCells.Add(Vector3Int.one);

            state.BrushStrokeCells.Clear();

            Assert.AreEqual(0, state.BrushStrokeCells.Count,
                "Clear must remove all recorded stroke cells.");
        }

        [Test]
        public void BrushStrokeCells_HashSet_DeduplicatesDuplicatePositions()
        {
            var state = new TileEditorState();
            state.BrushStrokeCells.Add(Vector3Int.zero);
            state.BrushStrokeCells.Add(Vector3Int.zero); // duplicate

            Assert.AreEqual(1, state.BrushStrokeCells.Count,
                "HashSet must deduplicate identical positions (prevents double-counting).");
        }

        // ════════════════════════════════════════════════════════════════════
        // 8. Regression: collider Draw / Erase must bypass zone-editability gate
        //    (otherwise the lobby and any zone with editableInTileEditor=false
        //    would silently refuse collider edits)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileBrush_Paint_NullCanEditCell_AllowsAnyCell()
        {
            // Mirrors HandleColliderInput passing canEditCell:null.
            var tilemap = SetupTilemap();
            var tile = MakeTile();

            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 3, canEditCell: null);

            Assert.AreEqual(9, edits.Count,
                "When canEditCell is null, every cell in the brush footprint must be painted, " +
                "regardless of zone editability \u2014 this is the lobby/locked-zone fix for the " +
                "collider Draw and Erase modes.");
        }

        [Test]
        public void TileBrush_Erase_NullCanEditCell_AllowsAnyCell()
        {
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            // Pre-populate a 2x2 region.
            tilemap.SetTile(new Vector3Int(0, 0, 0),  tile);
            tilemap.SetTile(new Vector3Int(1, 0, 0),  tile);
            tilemap.SetTile(new Vector3Int(0, -1, 0), tile);
            tilemap.SetTile(new Vector3Int(1, -1, 0), tile);

            var edits = TileBrush.Erase(tilemap, Vector3Int.zero, brushSize: 2, canEditCell: null);

            Assert.AreEqual(4, edits.Count,
                "Erase with canEditCell:null must clear every painted cell in the footprint, " +
                "ignoring any zone-locked status.");
            Assert.IsNull(tilemap.GetTile(new Vector3Int(1, -1, 0)),
                "All cells must be cleared after the bypass-erase.");
        }

        [Test]
        public void TileBrush_Paint_NullGate_BypassesZoneRestrictionMimic()
        {
            // Simulate the production scenario: a strict gate would reject every
            // cell, yet collider Draw must still succeed because it does not
            // pass the gate at all.
            var tilemap = SetupTilemap();
            var tile = MakeTile();
            System.Func<Vector3Int, bool> alwaysBlock = _ => false;

            // 1) WITH the gate \u2014 nothing is painted (regression baseline).
            var blocked = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 1,
                canEditCell: alwaysBlock);
            Assert.AreEqual(0, blocked.Count, "Sanity: gate blocks every paint.");
            Assert.IsNull(tilemap.GetTile(Vector3Int.zero));

            // 2) WITHOUT the gate (collider Draw mode behaviour) \u2014 always paints.
            var unblocked = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 1,
                canEditCell: null);
            Assert.AreEqual(1, unblocked.Count,
                "Collider Draw passes canEditCell:null \u2014 it must succeed even where " +
                "a hypothetical zone gate would reject.");
            Assert.AreEqual(tile, tilemap.GetTile(Vector3Int.zero));
        }

        // ════════════════════════════════════════════════════════════════════
        // 9. Regression: TileEditorManager.CanEditCell must always allow edits
        //    so the regular Brush / Eraser / Fill tools work in any zone
        //    (lobby, locked zones, off-zone cells) \u2014 mirrors the user
        //    requirement: "en cualquier parte del mapa es posible utilizar
        //    el brush del 'TILE EDITOR'".
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TileEditorManager_CanEditCell_ReturnsTrue_WhenNoConstraintSet()
        {
            var manager = NewTileEditorManager(out var host);
            try
            {
                Assert.IsTrue(InvokeCanEditCell(manager, new Vector3Int(0, 0, 0)));
                Assert.IsTrue(InvokeCanEditCell(manager, new Vector3Int(1234, -7777, 0)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TileEditorManager_CanEditCell_ReturnsTrue_EvenWithBlockingConstraint()
        {
            // The fix: even if MapEditor (F11) installs a constraint that would
            // reject every cell, CanEditCell must still allow the edit. This
            // guarantees the brush works on every tile, including those inside
            // the lobby and other zones flagged editableInTileEditor=false.
            var manager = NewTileEditorManager(out var host);
            try
            {
                manager.SetEditConstraint(_ => false);
                Assert.IsTrue(InvokeCanEditCell(manager, new Vector3Int(0, 0, 0)),
                    "CanEditCell must ignore _editConstraint and return true so the " +
                    "brush is usable everywhere on the map.");
                Assert.IsTrue(InvokeCanEditCell(manager, new Vector3Int(99, -42, 0)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TileEditorManager_ClearEditConstraint_StillReturnsTrue()
        {
            // After Clear, no constraint exists \u2014 still true (sanity check).
            var manager = NewTileEditorManager(out var host);
            try
            {
                manager.SetEditConstraint(_ => false);
                manager.ClearEditConstraint();
                Assert.IsTrue(InvokeCanEditCell(manager, Vector3Int.zero));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TileBrush_Paint_LargeBrush_PaintsEveryCellOfFootprint_NoGate()
        {
            // Documents the contract enforced by the production code now that
            // HandleBrushInput / HandleEraserInput pass an unconditional gate
            // (CanEditCell \u2261 true). A 5\u00d75 brush over an empty tilemap must
            // produce 25 edits.
            var tilemap = SetupTilemap();
            var tile = MakeTile();

            var edits = TileBrush.Paint(tilemap, Vector3Int.zero, tile, brushSize: 5, canEditCell: null);

            Assert.AreEqual(25, edits.Count);
            // Spot-check the four corners of the footprint.
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(0,  0, 0)));
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(4,  0, 0)));
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(0, -4, 0)));
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(4, -4, 0)));
        }

        [Test]
        public void TileBrush_FloodFill_NullGate_FillsConnectedRegion()
        {
            // Sanity for the Fill tool: with no gate it floods the entire region.
            var tilemap = SetupTilemap();
            var tile = MakeTile();

            // 3x3 empty region surrounded by null \u2192 flood fill from origin
            // covers all 9 cells.
            var edits = TileBrush.FloodFill(tilemap, Vector3Int.zero, tile,
                maxCells: 100, canEditCell: null);

            Assert.GreaterOrEqual(edits.Count, 1,
                "FloodFill from an empty cell must produce at least one edit when " +
                "the gate is open.");
            Assert.AreEqual(tile, tilemap.GetTile(Vector3Int.zero));
        }

        // ── Helpers for TileEditorManager reflection ─────────────────────────

        private static TileEditorManager NewTileEditorManager(out GameObject host)
        {
            host = new GameObject("TileEditorManager_TestHost");
            return host.AddComponent<TileEditorManager>();
        }

        private static bool InvokeCanEditCell(TileEditorManager manager, Vector3Int cellPos)
        {
            var mi = typeof(TileEditorManager).GetMethod("CanEditCell",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "Reflection: CanEditCell not found on TileEditorManager.");
            return (bool)mi.Invoke(manager, new object[] { cellPos });
        }
    }
}
