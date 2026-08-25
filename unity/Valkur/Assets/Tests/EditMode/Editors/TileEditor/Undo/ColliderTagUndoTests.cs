using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Regression coverage for Bug 2 of the metadata-undo fix: Ctrl+Z on a
    /// Collision-layer paint/erase stroke restored the collider TILE (that half
    /// already worked) but left <see cref="CollisionTagMap"/> untouched — a
    /// painted non-wildcard tag either survived as an orphaned entry, or (in the
    /// Move-To-Layer phase-C case) a cleared tag stayed cleared, so the restored
    /// collider silently fell back to <see cref="CollisionTagMap.Wildcard"/>
    /// instead of its real prior value.
    ///
    /// Two production call sites are covered:
    ///   1. <c>TileEditorManager.ApplyTagToEdits</c> — invoked via reflection
    ///      (private) against a real <see cref="TileEditorManager"/> instance,
    ///      reproducing <c>HandleColliderInput</c>'s exact
    ///      StartStroke → Paint → RecordEdits → ApplyTagToEdits → RecordMetadataEdits → EndStroke
    ///      sequence. <c>HandleColliderInput</c> itself can't be driven directly
    ///      in EditMode — it gates every branch behind
    ///      <c>MouseInputManager.WasLeftMouseButtonPressedThisFrame()</c>, which
    ///      falls through to <c>UnityEngine.Input</c>, unusable outside Play
    ///      Mode — so this reproduces its body instead of re-implementing the
    ///      fix under test. Mirrors the reflection convention already used by
    ///      <c>TileEditorColliderTests.InvokeCanEditCell</c> and
    ///      <c>TileEditorUndoRobustnessTests.InvokePrivate</c>.
    ///   2. <c>TileEditorManager.OnMoveToLayerClicked</c> (Phase C) — this one
    ///      IS <c>internal</c>, so it's called directly (no reflection) thanks
    ///      to <c>[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]</c> on
    ///      Valkur.Gameplay. Harness mirrors
    ///      <c>TileEditorUndoRobustnessTests.AttachWorldGrid</c>.
    /// </summary>
    [TestFixture]
    public class ColliderTagUndoTests
    {
        private GameObject _host;
        private GameObject _standaloneGrid;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_standaloneGrid != null) Object.DestroyImmediate(_standaloneGrid);
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. Direct collider paint — TileEditorManager.ApplyTagToEdits
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void ColliderPaint_NonWildcardTag_CtrlZ_ClearsTileAndOrphanedTag()
        {
            var manager = NewManager();
            var tilemap = NewStandaloneTilemap();
            var undo = new TileEditorUndoSystem();
            var tile = MakeTile("wall");
            var cell = new Vector3Int(1, 1, 0);

            manager.State.ActiveCollisionTag = "2"; // non-wildcard

            PaintColliderStroke(manager, undo, tilemap, cell, tile, drawing: true);

            // Sanity — the stroke actually painted and tagged the cell.
            Assert.AreEqual(tile, tilemap.GetTile(cell));
            Assert.AreEqual("2", manager.CollisionTags.GetRaw(cell));

            undo.Undo();

            Assert.IsNull(tilemap.GetTile(cell), "Undo must clear the collider tile (already worked pre-fix).");
            Assert.IsNull(manager.CollisionTags.GetRaw(cell),
                "BUG 2 — Undo must ALSO clear the explicit tag entry. Before the fix this stayed " +
                "'2' forever: an orphaned tag string invisible until the next paint or save.");
            Assert.AreEqual(CollisionTagMap.Wildcard, manager.CollisionTags.Get(cell),
                "With no explicit entry, Get() must fall back to the documented default.");
        }

        [Test]
        public void ColliderPaint_CtrlZ_ThenRedo_ReappliesTileAndTag()
        {
            var manager = NewManager();
            var tilemap = NewStandaloneTilemap();
            var undo = new TileEditorUndoSystem();
            var tile = MakeTile("wall");
            var cell = new Vector3Int(3, 3, 0);

            manager.State.ActiveCollisionTag = "4";
            PaintColliderStroke(manager, undo, tilemap, cell, tile, drawing: true);
            undo.Undo();
            Assert.IsNull(manager.CollisionTags.GetRaw(cell), "Pre-condition: undone.");

            undo.Redo();

            Assert.AreEqual(tile, tilemap.GetTile(cell), "Redo must re-place the collider tile.");
            Assert.AreEqual("4", manager.CollisionTags.GetRaw(cell),
                "Redo must ALSO re-stamp the tag — TileEditBatch.Redo() walks MetadataEdits forward.");
        }

        [Test]
        public void ColliderPaintEraseRepaint_Chain_UndoTwice_RestoresOriginalNonWildcardTag()
        {
            // Draw(tag "5") → Erase → Draw(tag "2"), each its own committed stroke.
            // Undoing the last two strokes must walk back through the erase and
            // land on the ORIGINAL tag "5" — not Wildcard, not "2".
            var manager = NewManager();
            var tilemap = NewStandaloneTilemap();
            var undo = new TileEditorUndoSystem();
            var tile = MakeTile("wall");
            var cell = new Vector3Int(5, 0, 0);

            manager.State.ActiveCollisionTag = "5";
            PaintColliderStroke(manager, undo, tilemap, cell, tile, drawing: true); // stroke 1: draw, tag 5

            manager.State.ActiveCollisionTag = null; // Erase mode doesn't read the tag
            PaintColliderStroke(manager, undo, tilemap, cell, tileToPaint: null, drawing: false); // stroke 2: erase

            manager.State.ActiveCollisionTag = "2";
            PaintColliderStroke(manager, undo, tilemap, cell, tile, drawing: true); // stroke 3: draw, tag 2

            Assert.AreEqual("2", manager.CollisionTags.GetRaw(cell), "Pre-condition: latest paint holds tag 2.");

            undo.Undo(); // reverts stroke 3 → back to the erased state
            Assert.IsNull(tilemap.GetTile(cell), "After 1x undo: tile erased again.");
            Assert.IsNull(manager.CollisionTags.GetRaw(cell), "After 1x undo: no explicit tag (erase state).");

            undo.Undo(); // reverts stroke 2 (the erase) → restores tile AND its original tag
            Assert.AreEqual(tile, tilemap.GetTile(cell), "After 2x undo: tile restored.");
            Assert.AreEqual("5", manager.CollisionTags.GetRaw(cell),
                "BUG 2 — After undoing the erase, the tag must come back as the ORIGINAL '5', " +
                "not left cleared (which would silently resolve to Wildcard on read).");
        }

        [Test]
        public void ColliderPaint_SameTagRepainted_ProducesNoMetadataEdit_UndoOnlyUndoesOnce()
        {
            // Guards the `if (oldRaw == newRaw) continue;` no-op branch in
            // ApplyTagToEdits: repainting the identical tile+tag must not push a
            // spurious empty batch onto the stack (HasContent must see it as empty).
            var manager = NewManager();
            var tilemap = NewStandaloneTilemap();
            var undo = new TileEditorUndoSystem();
            var tile = MakeTile("wall");
            var otherTile = MakeTile("wall2"); // different reference so TileBrush.Paint records an edit
            var cell = new Vector3Int(9, 9, 0);

            manager.State.ActiveCollisionTag = "3";
            PaintColliderStroke(manager, undo, tilemap, cell, tile, drawing: true);

            // Second "stroke": different visual tile, SAME tag → TileEdit exists but
            // no MetadataEdit should be recorded (tag identical to what's already stored).
            var tagEdits = PaintColliderStroke(manager, undo, tilemap, cell, otherTile, drawing: true);
            Assert.IsEmpty(tagEdits, "Repainting with the same active tag must record zero MetadataEdits.");

            undo.Undo(); // undoes stroke 2 (visual-only batch: tile reverts, tag untouched because none was recorded)
            Assert.AreEqual(tile, tilemap.GetTile(cell));
            Assert.AreEqual("3", manager.CollisionTags.GetRaw(cell), "Tag was never touched by stroke 2's undo.");

            undo.Undo(); // undoes stroke 1
            Assert.IsNull(tilemap.GetTile(cell));
            Assert.IsNull(manager.CollisionTags.GetRaw(cell));
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. Move-To-Layer Phase C — TileEditorManager.OnMoveToLayerClicked
        //    ("Idem para Move-To-Layer sobre una celda con collider tageado")
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void MoveToLayer_ErasesTaggedCollider_CtrlZ_RestoresTileAndOriginalTag()
        {
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var groundTm);
                var wgb = GetWorldGridBuilder(manager);
                var collisionTm = wgb.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);
                var destTm = wgb.GetTilemap(TilemapLayerSetup.TilemapLayer.FloorDecals);

                var visualTile = MakeTile("groundVisual");
                var colliderTile = MakeTile("wall");
                var cell = new Vector3Int(0, 0, 0);

                groundTm.SetTile(cell, visualTile);
                collisionTm.SetTile(cell, colliderTile);
                // Force _collisionTagMap into existence (mirrors what Init()/a prior
                // Draw stroke would have done) and seed a non-wildcard tag — this is
                // the exact state a real authored zone would be in.
                manager.CollisionTags.Set(cell, "5");
                manager.State.SelectedCells.Add(cell);

                manager.OnMoveToLayerClicked(TilemapLayerSetup.TilemapLayer.FloorDecals);

                // Pre-condition: move + Phase-C erase actually happened.
                Assert.IsNull(groundTm.GetTile(cell), "Source cleared.");
                Assert.AreEqual(visualTile, destTm.GetTile(cell), "Destination holds the moved tile.");
                Assert.IsNull(collisionTm.GetTile(cell), "Phase C erased the collider.");
                Assert.IsNull(manager.CollisionTags.GetRaw(cell), "Phase C cleared the explicit tag.");

                var undo = GetUndo(manager);
                undo.Undo();

                Assert.AreEqual(visualTile, groundTm.GetTile(cell), "Undo restores the source visual tile.");
                Assert.IsNull(destTm.GetTile(cell), "Undo clears the destination.");
                Assert.AreEqual(colliderTile, collisionTm.GetTile(cell), "Undo restores the collider tile.");
                Assert.AreEqual("5", manager.CollisionTags.GetRaw(cell),
                    "BUG 2 — Undo must restore the collider's ORIGINAL tag ('5'). Before the fix " +
                    "the tile came back but the tag stayed cleared, so the restored collider " +
                    "silently fell back to CollisionTagMap.Wildcard instead of its real value.");
                Assert.AreEqual("5", manager.CollisionTags.Get(cell),
                    "Read-path confirmation: Get() must resolve to '5', not Wildcard.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void MoveToLayer_ErasesTaggedCollider_CtrlZ_ThenRedo_ReClearsTagAgain()
        {
            var manager = NewManagerWithUndo(out var host);
            try
            {
                AttachWorldGrid(manager, out var groundTm);
                var wgb = GetWorldGridBuilder(manager);
                var collisionTm = wgb.GetTilemap(TilemapLayerSetup.TilemapLayer.Collision);

                var visualTile = MakeTile("groundVisual2");
                var colliderTile = MakeTile("wall2");
                var cell = new Vector3Int(1, 1, 0);

                groundTm.SetTile(cell, visualTile);
                collisionTm.SetTile(cell, colliderTile);
                manager.CollisionTags.Set(cell, "7");
                manager.State.SelectedCells.Add(cell);

                manager.OnMoveToLayerClicked(TilemapLayerSetup.TilemapLayer.WallsBottom);

                var undo = GetUndo(manager);
                undo.Undo();
                Assert.AreEqual("7", manager.CollisionTags.GetRaw(cell), "Pre-condition: restored by undo.");

                undo.Redo();

                Assert.IsNull(collisionTm.GetTile(cell), "Redo must re-erase the collider tile.");
                Assert.IsNull(manager.CollisionTags.GetRaw(cell),
                    "Redo must ALSO re-clear the tag — symmetric with the Undo fix.");
            }
            finally { Object.DestroyImmediate(host); }
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private TileEditorManager NewManager()
        {
            _host = new GameObject("ColliderTagUndoTests_Host");
            return _host.AddComponent<TileEditorManager>();
        }

        private static TileEditorManager NewManagerWithUndo(out GameObject host)
        {
            host = new GameObject("ColliderTagUndoTests_ManagerHost");
            var manager = host.AddComponent<TileEditorManager>();
            var fi = typeof(TileEditorManager).GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi != null && fi.GetValue(manager) == null)
                fi.SetValue(manager, new TileEditorUndoSystem());
            return manager;
        }

        private static TileEditorUndoSystem GetUndo(TileEditorManager m)
        {
            return (TileEditorUndoSystem)typeof(TileEditorManager)
                .GetField("_undo", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(m);
        }

        private static WorldGridBuilder GetWorldGridBuilder(TileEditorManager m)
        {
            return (WorldGridBuilder)typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(m);
        }

        /// <summary>Creates a Grid + one child Tilemap per TilemapLayer (named after the
        /// enum, matching WorldGridBuilder.GetTilemap's transform.Find lookup) and wires
        /// it into the manager. Duplicated from TileEditorUndoRobustnessTests.AttachWorldGrid
        /// (project convention: small test harnesses are copied per-file, not shared).</summary>
        private void AttachWorldGrid(TileEditorManager manager, out Tilemap groundTilemap)
        {
            var gridGo = new GameObject("WorldGrid");
            gridGo.transform.SetParent(manager.transform, false);
            var grid = gridGo.AddComponent<Grid>();
            var wgb = gridGo.AddComponent<WorldGridBuilder>();

            groundTilemap = null;
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

            typeof(TileEditorManager)
                .GetField("worldGridBuilder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, wgb);
            typeof(WorldGridBuilder)
                .GetField("_grid", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(wgb, grid);

            manager.State.CurrentLayer = TilemapLayerSetup.TilemapLayer.Ground;
        }

        private Tilemap NewStandaloneTilemap()
        {
            _standaloneGrid = new GameObject("StandaloneGrid");
            _standaloneGrid.AddComponent<Grid>();
            var tmGo = new GameObject("StandaloneTilemap");
            tmGo.transform.SetParent(_standaloneGrid.transform, false);
            return tmGo.AddComponent<Tilemap>();
        }

        private static Tile MakeTile(string name)
        {
            var tex = new Texture2D(2, 2);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1f);
            sprite.name = name;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.name = name;
            return tile;
        }

        /// <summary>Reproduces HandleColliderInput's per-click body verbatim (real
        /// TileBrush.Paint + real, reflected ApplyTagToEdits + real TileEditorUndoSystem)
        /// without depending on MouseInputManager frame polling, which is unusable in
        /// EditMode. Returns the MetadataEdit list recorded, for assertion convenience.</summary>
        private static List<MetadataEdit> PaintColliderStroke(TileEditorManager manager,
            TileEditorUndoSystem undo, Tilemap collisionTm, Vector3Int cell, TileBase tileToPaint, bool drawing)
        {
            undo.StartStroke(collisionTm);
            var edits = TileBrush.Paint(collisionTm, cell, tileToPaint, brushSize: 1, canEditCell: null);
            undo.RecordEdits(edits);
            var tagEdits = InvokeApplyTagToEdits(manager, edits, drawing);
            undo.RecordMetadataEdits(tagEdits);
            undo.EndStroke();
            return tagEdits;
        }

        private static List<MetadataEdit> InvokeApplyTagToEdits(TileEditorManager manager,
            List<TileEdit> edits, bool drawing)
        {
            var mi = typeof(TileEditorManager).GetMethod("ApplyTagToEdits",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mi, "Reflection: ApplyTagToEdits not found on TileEditorManager.");
            return (List<MetadataEdit>)mi.Invoke(manager, new object[] { edits, drawing });
        }
    }
}
