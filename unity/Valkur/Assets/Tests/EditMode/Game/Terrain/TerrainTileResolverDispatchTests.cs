using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Tests <see cref="TerrainTileResolver.ResolveVariantForCell"/>'s model
    /// dispatch — the single place that decides whether a cell resolves against
    /// the cardinal mask (Blob16) or the corner mask (Corner16), driven purely by
    /// <see cref="TilesetRuleset.Model"/>. Proves the two models coexist without
    /// either one hijacking the other's ruleset — the exact risk of bolting a
    /// second auto-tile model onto a solver that previously only knew one.
    /// </summary>
    [TestFixture]
    public class TerrainTileResolverDispatchTests
    {
        private readonly List<Object> _scriptableObjects = new List<Object>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        [TearDown]
        public void TearDown()
        {
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
        }

        private Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            _sprites.Add(sprite);
            return sprite;
        }

        private TilesetRuleset NewBlobRuleset(string folder, string primary)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, primary, null, 0, AutoTileModel.Blob16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Blob16Slot)i, new[] { NewSprite($"{folder}_blob{i}") });
            return rs;
        }

        private TilesetRuleset NewCornerRuleset(string folder, string primary, string secondary)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, primary, secondary, 0, AutoTileModel.Corner16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Corner16Slot)i, new[] { NewSprite($"{folder}_corner{i}") });
            return rs;
        }

        private static Dictionary<Vector2Int, string> Grid(params (int x, int y, string t)[] cells)
        {
            var d = new Dictionary<Vector2Int, string>(cells.Length);
            foreach (var (x, y, t) in cells) d[new Vector2Int(x, y)] = t;
            return d;
        }

        [Test]
        public void NullRuleset_ReturnsNull()
        {
            var grid = Grid((0, 0, "grass"));
            var result = TerrainTileResolver.ResolveVariantForCell(null, grid, new Vector2Int(0, 0), "grass", 0);
            Assert.IsNull(result);
        }

        [Test]
        public void Corner16_WithoutSecondaryTerrain_ReturnsNull()
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata("broken_corner", "grass", null, 0, AutoTileModel.Corner16);
            for (int i = 0; i < 16; i++)
                rs.EditorSetSlot((Corner16Slot)i, new[] { NewSprite($"broken_corner_{i}") });

            var grid = Grid((0, 0, "grass"));
            var result = TerrainTileResolver.ResolveVariantForCell(rs, grid, new Vector2Int(0, 0), "grass", 0);
            Assert.IsNull(result, "A Corner16 ruleset with no secondary terrain has nothing to test corners against.");
        }

        [Test]
        public void Blob16Model_UsesCardinalMask_DiagonalNeighborsAreIgnored()
        {
            // All 4 CARDINAL neighbours are "grass" (-> cardinal mask = Center),
            // all 4 DIAGONAL neighbours are a different terrain entirely. A Blob16
            // ruleset must resolve to Center regardless — corners never enter into it.
            var rs = NewBlobRuleset("grass", "grass");
            var grid = Grid(
                (0, 0, "grass"),
                (0, 1, "grass"), (1, 0, "grass"), (0, -1, "grass"), (-1, 0, "grass"),
                (1, 1, "dirt"), (-1, 1, "dirt"), (1, -1, "dirt"), (-1, -1, "dirt"));

            var result = TerrainTileResolver.ResolveVariantForCell(rs, grid, new Vector2Int(0, 0), "grass", 0);
            Assert.AreSame(rs.GetVariants(Blob16Slot.Center)[0], result);
        }

        [Test]
        public void Corner16Model_UsesCornerMaskAgainstSecondaryTerrain_NotCardinalMaskAgainstPassedTerrain()
        {
            // Center is "grass" (primary); ALL 8 neighbours (cardinal AND
            // diagonal) are "dirt" (secondary). If this were wrongly dispatched
            // through the cardinal path against the PASSED terrain ("grass"), no
            // neighbour would match it -> Isolated/CornerNone. The correct
            // Corner16 dispatch tests corners against ruleset.TerrainSecondary
            // ("dirt") instead, which every corner's 2x2 block satisfies 3-of-4 ->
            // CornerFull.
            var rs = NewCornerRuleset("grass_dirt", "grass", "dirt");
            var grid = Grid(
                (0, 0, "grass"),
                (0, 1, "dirt"), (1, 0, "dirt"), (0, -1, "dirt"), (-1, 0, "dirt"),
                (1, 1, "dirt"), (-1, 1, "dirt"), (1, -1, "dirt"), (-1, -1, "dirt"));

            var result = TerrainTileResolver.ResolveVariantForCell(rs, grid, new Vector2Int(0, 0), "grass", 0);
            Assert.AreSame(rs.GetVariants(Corner16Slot.CornerFull)[0], result,
                "Corner16 must resolve against TerrainSecondary via the corner mask, not against the " +
                "'terrain' parameter via the cardinal mask.");
        }

        [Test]
        public void UnassignedResolvedSlot_ReturnsNull_BothModels()
        {
            var blobRs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(blobRs);
            blobRs.EditorSetMetadata("empty_blob", "grass", null, 0, AutoTileModel.Blob16);

            var cornerRs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(cornerRs);
            cornerRs.EditorSetMetadata("empty_corner", "grass", "dirt", 0, AutoTileModel.Corner16);

            var grid = Grid((0, 0, "grass"));
            Assert.IsNull(TerrainTileResolver.ResolveVariantForCell(blobRs, grid, new Vector2Int(0, 0), "grass", 0));
            Assert.IsNull(TerrainTileResolver.ResolveVariantForCell(cornerRs, grid, new Vector2Int(0, 0), "grass", 0));
        }
    }
}
