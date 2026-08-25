using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Editors.TileEditor.Undo
{
    /// <summary>
    /// Full-cycle Undo/Redo SYMMETRY coverage for the parallel-metadata undo
    /// mechanism (<see cref="MetadataEdit"/> / <see cref="TileEditBatch.MetadataEdits"/>)
    /// across all three metadata sinks it serves: <see cref="TerrainMap"/>,
    /// <see cref="CollisionTagMap"/> and <see cref="LayerJumpMap"/>.
    ///
    /// Every test drives the same shape: seed a PRE-EXISTING (non-default) value,
    /// apply a stroke that changes it, then walk
    /// Apply -&gt; Undo -&gt; Undo(again, no-op) -&gt; Redo -&gt; Redo(again, no-op) -&gt; Undo(again)
    /// and assert the exact same two states (pre-stroke / post-stroke) reappear
    /// every time, on BOTH the visual tile (where one is involved) and the
    /// metadata map. This closes two gaps none of the sibling Bug-1/2/3 suites
    /// (<c>ColliderTagUndoTests</c>, <c>LayerJumpsUndoTests</c>,
    /// <c>AutoTileRegionUndoTests</c>) assert explicitly:
    ///   (a) round-tripping PAST a single Undo/Redo pair — does a SECOND cycle
    ///       reproduce the identical states, or does state drift/corrupt;
    ///   (b) calling Undo()/Redo() past the end of their respective stacks is a
    ///       true no-op — it must not touch the metadata map at all, not even a
    ///       redundant Set() of the same value.
    ///
    /// Drives <see cref="TileEditorUndoSystem"/> + <see cref="TileEditBatch"/>
    /// directly against real production metadata-map classes — no
    /// TileEditorManager instance is required, matching the convention already
    /// used by <c>AutoTileRegionUndoTests</c> and <c>TileEditBatchCrossTilemapTests</c>.
    /// </summary>
    [TestFixture]
    public class MetadataUndoRedoSymmetryTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<Object> _tiles = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();

            foreach (var t in _tiles)
                if (t != null) Object.DestroyImmediate(t);
            _tiles.Clear();
        }

        private Tile MakeTile(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = sprite;
            _tiles.Add(t);
            return t;
        }

        private Tilemap NewTilemap(string name)
        {
            var gridGo = new GameObject(name + "_Grid");
            _created.Add(gridGo);
            gridGo.AddComponent<Grid>().cellSize = Vector3.one;
            var tmGo = new GameObject(name + "_Tilemap");
            tmGo.transform.SetParent(gridGo.transform, false);
            return tmGo.AddComponent<Tilemap>();
        }

        // ════════════════════════════════════════════════════════════════════
        // TerrainMap + tile
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void TerrainMapAndTile_FullUndoRedoCycle_IsSymmetricAndIdempotentAtBothEnds()
        {
            var tilemap = NewTilemap("Terrain");
            var terrainMap = new TerrainMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(0, 0, 0);
            var priorTile = MakeTile(Color.green);
            var newTile = MakeTile(Color.yellow);

            // Pre-existing state, as if an earlier (already committed) stroke had
            // painted this cell — the round trip must land back on THIS, not on
            // a bare "cleared" cell.
            tilemap.SetTile(cell, priorTile);
            terrainMap.SetTerrain(cell, "dirt");

            undo.StartStroke(tilemap);
            tilemap.SetTile(cell, newTile);
            terrainMap.SetTerrain(cell, "grass");
            undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, priorTile, newTile) });
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "dirt", "grass", terrainMap) });
            undo.EndStroke();

            Assert.AreEqual(newTile, tilemap.GetTile(cell), "Sanity: stroke applied the new tile.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell), "Sanity: stroke applied the new terrain.");

            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "1st Undo must restore the PRE-STROKE tile.");
            Assert.AreEqual("dirt", terrainMap.GetTerrain(cell), "1st Undo must restore the PRE-STROKE terrain.");

            Assert.IsNull(undo.Undo(), "Undo stack is empty — a second Undo must be a no-op.");
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "A no-op Undo must not touch the tile.");
            Assert.AreEqual("dirt", terrainMap.GetTerrain(cell), "A no-op Undo must not touch the terrain.");

            Assert.IsNotNull(undo.Redo());
            Assert.AreEqual(newTile, tilemap.GetTile(cell), "Redo must re-apply the tile.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell), "Redo must re-apply the terrain.");

            Assert.IsNull(undo.Redo(), "Redo stack is empty — a second Redo must be a no-op.");
            Assert.AreEqual(newTile, tilemap.GetTile(cell), "A no-op Redo must not touch the tile.");
            Assert.AreEqual("grass", terrainMap.GetTerrain(cell), "A no-op Redo must not touch the terrain.");

            // Second full cycle — proves the round trip is repeatable, not a
            // one-shot artifact of the first Undo/Redo pair.
            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "2nd cycle: Undo again lands on the same pre-stroke tile.");
            Assert.AreEqual("dirt", terrainMap.GetTerrain(cell), "2nd cycle: Undo again lands on the same pre-stroke terrain.");
        }

        // ════════════════════════════════════════════════════════════════════
        // CollisionTagMap + tile
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void CollisionTagMapAndTile_FullUndoRedoCycle_IsSymmetricAndIdempotentAtBothEnds()
        {
            var tilemap = NewTilemap("Collision");
            var tagMap = new CollisionTagMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(3, 1, 0);
            var priorTile = MakeTile(Color.red);
            var newTile = MakeTile(Color.blue);

            tilemap.SetTile(cell, priorTile);
            tagMap.Set(cell, "2");

            undo.StartStroke(tilemap);
            tilemap.SetTile(cell, newTile);
            tagMap.Set(cell, "5");
            undo.RecordEdits(new List<TileEdit> { new TileEdit(cell, priorTile, newTile) });
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "2", "5", tagMap) });
            undo.EndStroke();

            Assert.AreEqual(newTile, tilemap.GetTile(cell), "Sanity: stroke applied the new tile.");
            Assert.AreEqual("5", tagMap.GetRaw(cell), "Sanity: stroke applied the new tag.");

            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "1st Undo must restore the pre-stroke tile.");
            Assert.AreEqual("2", tagMap.GetRaw(cell), "1st Undo must restore the pre-stroke tag.");

            Assert.IsNull(undo.Undo(), "Second Undo with nothing left must be a no-op.");
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "A no-op Undo must not touch the tile.");
            Assert.AreEqual("2", tagMap.GetRaw(cell), "A no-op Undo must not disturb the tag.");

            Assert.IsNotNull(undo.Redo());
            Assert.AreEqual(newTile, tilemap.GetTile(cell), "Redo must re-apply the tile.");
            Assert.AreEqual("5", tagMap.GetRaw(cell), "Redo must re-apply the tag.");

            Assert.IsNull(undo.Redo(), "Second Redo with nothing left must be a no-op.");
            Assert.AreEqual(newTile, tilemap.GetTile(cell), "A no-op Redo must not touch the tile.");
            Assert.AreEqual("5", tagMap.GetRaw(cell), "A no-op Redo must not disturb the tag.");

            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual(priorTile, tilemap.GetTile(cell), "2nd cycle: Undo again reproduces the same pre-stroke tile.");
            Assert.AreEqual("2", tagMap.GetRaw(cell), "2nd cycle: Undo again reproduces the same pre-stroke tag.");
        }

        // ════════════════════════════════════════════════════════════════════
        // LayerJumpMap — metadata only, no tilemap involved (mirrors production)
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void LayerJumpMetadataOnly_FullUndoRedoCycle_IsSymmetricAndIdempotentAtBothEnds()
        {
            var jumpMap = new LayerJumpMap();
            var undo = new TileEditorUndoSystem();
            var cell = new Vector3Int(7, 7, 0);

            jumpMap.Set(cell, "3"); // pre-existing target from an earlier stroke

            undo.StartStroke(null); // Layer-Jumps strokes never involve a tilemap
            jumpMap.Set(cell, "6");
            undo.RecordMetadataEdits(new List<MetadataEdit> { new MetadataEdit(cell, "3", "6", jumpMap) });
            undo.EndStroke();

            Assert.AreEqual("6", jumpMap.Get(cell), "Sanity: stroke applied the new target.");

            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual("3", jumpMap.Get(cell), "1st Undo must restore the pre-stroke target.");

            Assert.IsNull(undo.Undo(), "Second Undo with nothing left must be a no-op.");
            Assert.AreEqual("3", jumpMap.Get(cell), "A no-op Undo must not disturb the target.");

            Assert.IsNotNull(undo.Redo());
            Assert.AreEqual("6", jumpMap.Get(cell), "Redo must re-apply the target.");

            Assert.IsNull(undo.Redo(), "Second Redo with nothing left must be a no-op.");
            Assert.AreEqual("6", jumpMap.Get(cell), "A no-op Redo must not disturb the target.");

            Assert.IsNotNull(undo.Undo());
            Assert.AreEqual("3", jumpMap.Get(cell), "2nd cycle: Undo again reproduces the same pre-stroke target.");
        }
    }
}
