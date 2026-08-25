using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Integration coverage for <see cref="TerrainPainter"/> at the boundary
    /// where Corner16 rulesets currently meet it.
    ///
    /// <para>
    /// <b>Known gap, verified here on purpose.</b> Both <see cref="TerrainPainter.PaintRegion"/>
    /// and <see cref="TerrainPainter.PaintCell"/> select a ruleset for a painted
    /// cell via <see cref="TerrainCatalog.FindBaseRuleset"/>, which explicitly
    /// excludes every <c>IsTransition</c> ruleset (<c>if (r.IsTransition) continue;</c>)
    /// — and a Corner16 ruleset is BY DEFINITION a transition (it always needs
    /// <see cref="TilesetRuleset.TerrainSecondary"/>). <see cref="TerrainCatalog.FindTransitionRuleset"/>
    /// exists but is called from nowhere in production code. So as of today,
    /// painting a terrain whose only registered ruleset is Corner16 behaves
    /// EXACTLY like painting a terrain with no ruleset at all: the terrain is
    /// stamped into the <see cref="TerrainMap"/>, but zero <see cref="TileEdit"/>s
    /// are produced. This is the exact gap <c>TileEditorManager.ResolveAutoBrushTerrain</c>
    /// defends against by checking <c>FindBaseRuleset(primary) != null</c> before
    /// letting the AUTO brush paint (see <c>AutoBrushTerrainResolutionTests</c>).
    /// Wiring an actual selection path for Corner16 rulesets is out of scope for
    /// this test suite (belongs to whoever wires the corner16 catalog data in);
    /// this test exists so that work gets a clear, named failure to update
    /// instead of a silent behavior change.
    /// </para>
    ///
    /// Corner16's actual per-cell correctness (once a ruleset IS in hand) is
    /// fully covered at the resolver level by <c>Corner16RoundTripTests</c> and
    /// <c>TerrainTileResolverDispatchTests</c> — <see cref="TerrainTileResolver.ResolveVariantForCell"/>
    /// takes the ruleset as a parameter and doesn't care how the caller found it.
    /// </summary>
    [TestFixture]
    public class TerrainPainterCorner16Tests
    {
        private const string Primary = "grass";
        private const string Secondary = "dirt";

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

            TileRegistry.Instance.Clear();
            TerrainCatalogLoader.InvalidateCache();
        }

        private Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private TilesetRuleset NewCornerRulesetWithAllSlots(string folder)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, Primary, Secondary, 0, AutoTileModel.Corner16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Corner16Slot)i, new[] { NewSprite($"{folder}_corner{i}") });
            return rs;
        }

        private TilesetRuleset NewBlobRulesetWithAllSlots(string folder)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, Primary, null, 0, AutoTileModel.Blob16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_blob{i}") });
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

        [Test]
        public void PaintRegion_TerrainWhoseOnlyRulesetIsACorner16Transition_StampsTerrainButProducesNoTileEdits()
        {
            var corner = NewCornerRulesetWithAllSlots("grass_dirt_corner");
            var catalog = NewCatalog(corner); // ONLY a transition ruleset is registered
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            var rect = new BoundsInt(0, 0, 0, 3, 3, 1);
            var (edits, metadataEdits) = TerrainPainter.PaintRegion(tilemap, rect, Secondary, catalog, map);

            Assert.IsEmpty(edits,
                "KNOWN GAP: FindBaseRuleset excludes every transition ruleset, so a Corner16-only " +
                "catalog entry is never selected by PaintRegion today — see class doc.");
            Assert.AreEqual(9, metadataEdits.Count, "Terrain is still stamped even though no sprite resolves.");
            Assert.AreEqual(Secondary, map.GetTerrain(new Vector2Int(1, 1)));
        }

        [Test]
        public void PaintCell_RingLoop_CoversAllEightNeighboursIncludingDiagonals()
        {
            // Model-agnostic proof of PaintCell's documented ring shape. A BASE
            // (non-transition) Blob16 ruleset IS reachable through
            // FindBaseRuleset today (unlike Corner16 — see the gap test above),
            // so this exercises the real loop geometry PaintCell relies on: the
            // ring around a 1x1 rect is a full 3x3 block, not a plus/cross of
            // only the 4 cardinal neighbours. That geometry is exactly what a
            // Corner16 ruleset will need once its own selection gap is closed.
            var rs = NewBlobRulesetWithAllSlots("grass_blob");
            var catalog = NewCatalog(rs);
            var map = new TerrainMap();
            var tilemap = NewTilemap();

            Vector2Int[] neighbours =
            {
                new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
                new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1),
            };

            // Pre-seed every one of the 8 neighbours (cardinal AND diagonal) of
            // (0,0) with "grass" terrain but a deliberately WRONG stale tile, so
            // the ring pass is forced to actually overwrite each one to prove it
            // was visited.
            var staleTile = ScriptableObject.CreateInstance<Tile>();
            _scriptableObjects.Add(staleTile);
            foreach (var n in neighbours)
            {
                map.SetTerrain(n, Primary);
                tilemap.SetTile(new Vector3Int(n.x, n.y, 0), staleTile);
            }

            TerrainPainter.PaintCell(tilemap, new Vector3Int(0, 0, 0), Primary, catalog, map);

            foreach (var n in neighbours)
            {
                var tile = tilemap.GetTile(new Vector3Int(n.x, n.y, 0));
                Assert.AreNotSame(staleTile, tile,
                    $"neighbour {n} (including diagonals) must be re-resolved by PaintCell's ring, not left stale.");
            }
        }
    }
}
