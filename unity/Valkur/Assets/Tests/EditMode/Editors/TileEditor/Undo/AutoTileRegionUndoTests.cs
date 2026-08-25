using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Regression coverage for Bug 1 of the metadata-undo fix: Ctrl+Z on an
    /// Auto-Tile Region stroke previously reverted only the visual sprites on
    /// the tilemap — the parallel <see cref="TerrainMap"/> was never rolled
    /// back, so the orphaned terrain string survived Undo, got written to the
    /// zone's .overlay.json on the next save, and reappeared after a restart
    /// (a terrain "ghost" the visible tiles no longer matched).
    ///
    /// These tests drive the EXACT call sequence
    /// <c>TileEditorManager.CommitAutoTileRegion</c> performs — real
    /// <see cref="TerrainPainter.PaintRegion"/> (unchanged production static
    /// method) feeding a real <see cref="TileEditorUndoSystem"/> via
    /// <c>StartStroke → RecordEdits → RecordMetadataEdits → EndStroke</c> —
    /// rather than hand-simulating SetTile/SetTerrain calls the way the older
    /// <c>TileEditBatchCrossTilemapTests</c> suite does. The only piece NOT
    /// exercised is <c>CommitAutoTileRegion</c> itself (private, MonoBehaviour,
    /// gated behind a mouse-drag + <c>TerrainCatalogLoader.Load()</c> reading
    /// from Resources) — its body is nothing but this same four-call sequence,
    /// so nothing observable is left uncovered. See class doc of
    /// <c>TileEditBatchCrossTilemapTests</c> for the project's standing
    /// rationale on why the manager itself isn't spun up for this kind of test.
    /// </summary>
    [TestFixture]
    public class AutoTileRegionUndoTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Object> _scriptableObjects = new List<Object>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            foreach (var s in _sprites)
            {
                if (s == null) continue;
                if (s.texture != null) Object.DestroyImmediate(s.texture);
                Object.DestroyImmediate(s);
            }
            _sprites.Clear();

            foreach (var so in _scriptableObjects)
                if (so != null) Object.DestroyImmediate(so);
            _scriptableObjects.Clear();

            // Same hygiene as TerrainPainterTests: TileRegistry caches Tile
            // instances by sprite name across the whole EditMode session, so a
            // destroyed sprite from this test must not leak into the next one.
            TileRegistry.Instance.Clear();
            TerrainCatalogLoader.InvalidateCache();
        }

        // ── Fixture builders (mirrors TerrainPainterTests' pattern) ──────────

        private Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private TilesetRuleset NewRulesetWithAllSlots(string folder, string terrain, int priority)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, terrain, null, priority, AutoTileModel.Blob16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_slot{i}") });
            return rs;
        }

        private TerrainCatalog NewCatalog(params TilesetRuleset[] rulesets)
        {
            var catalog = ScriptableObject.CreateInstance<TerrainCatalog>();
            _scriptableObjects.Add(catalog);
            foreach (var rs in rulesets) catalog.EditorAdd(rs);
            return catalog;
        }

        private Tilemap NewTilemap()
        {
            var gridGo = new GameObject("Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>();
            var tilemapGo = new GameObject("Tilemap");
            tilemapGo.transform.SetParent(gridGo.transform);
            _created.Add(tilemapGo);
            return tilemapGo.AddComponent<Tilemap>();
        }

        /// <summary>Reproduces TileEditorManager.CommitAutoTileRegion's body verbatim
        /// against real production classes (no manager instance required).</summary>
        private static void CommitAutoTileRegionLike(TileEditorUndoSystem undo, Tilemap tilemap,
            BoundsInt rect, string terrain, TerrainCatalog catalog, TerrainMap terrainMap)
        {
            undo.StartStroke(tilemap);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, terrain, catalog, terrainMap);
            undo.RecordEdits(edits);
            undo.RecordMetadataEdits(metadataEdits);
            undo.EndStroke();
        }

        // ════════════════════════════════════════════════════════════════════
        // Core fix: full undo/redo round trip (ruleset present)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AutoTileRegion_CtrlZ_RevertsBothTilemapSpriteAndTerrainMap()
        {
            var rs = NewRulesetWithAllSlots("undoGrass", "grass", 0);
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(0, 0, 0);
            var rect = new BoundsInt(0, 0, 0, 1, 1, 1);

            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);

            // Pre-condition: the stroke actually did something visible AND in the terrain layer.
            Assert.IsNotNull(tilemap.GetTile(cell), "Sanity: region paint must place a sprite.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell), "Sanity: region paint must stamp the terrain.");

            undo.Undo();

            Assert.IsNull(tilemap.GetTile(cell),
                "Undo must clear the visual sprite (this half already worked pre-fix).");
            Assert.IsNull(terrainMap.GetTerrain(cell),
                "BUG 1 — Undo must ALSO clear the TerrainMap entry. Before the fix this stayed " +
                "'grass' forever: an orphaned terrain string that survived Ctrl+Z, got written " +
                "to the zone's .overlay.json on the next save, and reappeared after restart.");
        }

        [Test]
        public void AutoTileRegion_CtrlZ_ThenRedo_ReappliesTerrainAndSprite()
        {
            var rs = NewRulesetWithAllSlots("undoGrass2", "grass", 0);
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(2, 2, 0);
            var rect = new BoundsInt(2, 2, 0, 1, 1, 1);

            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);
            undo.Undo();
            Assert.IsNull(terrainMap.GetTerrain(cell), "Pre-condition: undone.");

            undo.Redo();

            Assert.IsNotNull(tilemap.GetTile(cell), "Redo must re-place the sprite.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell),
                "Redo must ALSO re-stamp the TerrainMap — TileEditBatch.Redo() walks " +
                "MetadataEdits forward exactly as it walks Edits forward.");
        }

        [Test]
        public void AutoTileRegion_MultiCellRect_Undo_RestoresEachCellIndependently()
        {
            var rs = NewRulesetWithAllSlots("undoGrass3", "grass", 0);
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();

            // Pre-seed (0,0) with a DIFFERENT terrain from an earlier (already-
            // committed) paint, so undo must restore a real prior value, not just
            // "null" everywhere — this catches an undo that blindly clears every
            // touched cell instead of replaying each cell's own OldValue.
            terrainMap.SetTerrain(new Vector2Int(0, 0), "dirt");

            var rect = new BoundsInt(0, 0, 0, 2, 1, 1); // cells (0,0) and (1,0)
            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);

            Assert.AreEqual("grass", terrainMap.GetTerrain(new Vector2Int(0, 0)));
            Assert.AreEqual("grass", terrainMap.GetTerrain(new Vector2Int(1, 0)));

            undo.Undo();

            Assert.AreEqual("dirt", terrainMap.GetTerrain(new Vector2Int(0, 0)),
                "Undo must restore the cell's PRIOR terrain ('dirt'), not just clear it.");
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(1, 0)),
                "The other cell had no prior terrain, so undo must clear it back to null.");
        }

        // ════════════════════════════════════════════════════════════════════
        // The audited "no ruleset" case: zero TileEdits, only MetadataEdits.
        // This is the sharpest reproduction of the shipped bug AND doubles as
        // coverage for the TileEditorUndoSystem.HasContent fix (a batch with an
        // empty Edits list used to be silently dropped by EndStroke).
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AutoTileRegion_TerrainWithoutRuleset_StrokeIsStillPushedToUndoStack()
        {
            var catalog = NewCatalog(); // no ruleset registered for "grass"
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var rect = new BoundsInt(0, 0, 0, 2, 2, 1);

            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);

            // Sanity: no ruleset → PaintRegion produced zero visual TileEdits, but
            // it DID stamp the terrain (see TerrainPainterTests.
            // PaintRegion_TerrainWithoutRuleset_DoesNothingButStampsTerrain).
            Assert.AreEqual("grass", terrainMap.GetTerrain(new Vector2Int(0, 0)));

            var undoneBatch = undo.Undo();

            Assert.IsNotNull(undoneBatch,
                "BUG 1, audited edge case + shared HasContent fix (also required by Bug 3's " +
                "Layer-Jumps strokes) — a batch whose Edits list is empty but whose " +
                "MetadataEdits list is non-empty must still be pushed onto the undo stack by " +
                "EndStroke. Before the fix, EndStroke only checked Edits.Count > 0, so this " +
                "exact stroke (auto-tile region over a terrain with no ruleset) was silently " +
                "discarded and Ctrl+Z had nothing to undo.");
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(0, 0)),
                "Undo must clear the orphaned terrain even though it never produced a visible sprite.");
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(1, 1)),
                "Every cell of the 2x2 rect must be reverted, not just the first.");
        }

        [Test]
        public void AutoTileRegion_TerrainWithoutRuleset_Redo_ReappliesTerrain()
        {
            var catalog = NewCatalog(); // no ruleset
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var rect = new BoundsInt(0, 0, 0, 1, 1, 1);

            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);
            undo.Undo();
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(0, 0)));

            var redoneBatch = undo.Redo();

            Assert.IsNotNull(redoneBatch, "A metadata-only batch must also be redo-able.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(new Vector2Int(0, 0)));
        }

        // ════════════════════════════════════════════════════════════════════
        // No-op guard: repainting the SAME terrain must not record a spurious
        // MetadataEdit (would otherwise pollute undo with a self-reverting entry).
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void AutoTileRegion_RepaintingSameTerrain_ProducesNoMetadataEdit_UndoIsNoOp()
        {
            var rs = NewRulesetWithAllSlots("undoGrass4", "grass", 0);
            var catalog = NewCatalog(rs);
            var terrainMap = new TerrainMap();
            var tilemap = NewTilemap();
            var undo = new TileEditorUndoSystem();
            var rect = new BoundsInt(0, 0, 0, 1, 1, 1);

            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap);
            CommitAutoTileRegionLike(undo, tilemap, rect, "grass", catalog, terrainMap); // same terrain again

            // Second stroke should have recorded nothing (terrain unchanged), so the
            // batch has neither Edits nor MetadataEdits and must NOT reach the undo
            // stack per HasContent — only ONE undo step should exist.
            var first = undo.Undo();
            Assert.IsNotNull(first, "The first (real) paint must still be on the stack.");
            Assert.IsNull(terrainMap.GetTerrain(new Vector2Int(0, 0)), "Fully reverted after the single real undo.");

            var second = undo.Undo();
            Assert.IsNull(second,
                "A no-op repaint (terrain unchanged, sprite unchanged) must not have pushed a " +
                "second, empty batch onto the undo stack.");
        }
    }
}
