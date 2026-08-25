using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// THE round-trip test for the Corner16 auto-tile model. Builds a synthetic
    /// terrain grid (an island of a secondary material inside a sea of a primary
    /// one), resolves the tile for every interior cell through the REAL
    /// production path (<see cref="TerrainTileResolver.ResolveVariantForCell"/>),
    /// and checks that the resolved sprite's corner signature matches what the
    /// grid itself dictates at that cell — computed here by an INDEPENDENT
    /// re-derivation of the corner-majority convention (from
    /// <see cref="BitmaskCalculator.CornerMask"/>'s own doc comment), never by
    /// calling back into the production mask function to grade itself.
    ///
    /// If the corner convention is wired wrong anywhere along
    /// grid -&gt; mask -&gt; slot -&gt; sprite (wrong bit order, wrong 2x2 block per
    /// corner, wrong tie-break, wrong model dispatch), this fails for SOME cell of
    /// SOME shape. If it's right, it passes for any shape — proven here with two
    /// unrelated ones: an axis-aligned cross and a diagonal watershed (the exact
    /// "ambiguous edge, unambiguous corner" case the Corner16 model exists for).
    /// </summary>
    [TestFixture]
    public class Corner16RoundTripTests
    {
        private const string Primary = "grass";
        private const string Secondary = "dirt";

        private readonly List<UnityEngine.Object> _scriptableObjects = new List<UnityEngine.Object>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        [TearDown]
        public void TearDown()
        {
            foreach (var s in _sprites)
            {
                if (s == null) continue;
                if (s.texture != null) UnityEngine.Object.DestroyImmediate(s.texture);
                UnityEngine.Object.DestroyImmediate(s);
            }
            _sprites.Clear();

            foreach (var so in _scriptableObjects)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
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

        /// <summary>Builds a Corner16 ruleset with one uniquely-tagged sprite per
        /// slot, keyed by its raw byte value for direct lookup against the
        /// independently derived expected mask.</summary>
        private (TilesetRuleset Ruleset, Dictionary<byte, Sprite> SpriteBySlot) NewTaggedCornerRuleset(string folder)
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            _scriptableObjects.Add(rs);
            rs.EditorSetMetadata(folder, Primary, Secondary, 0, AutoTileModel.Corner16);

            var bySlot = new Dictionary<byte, Sprite>(16);
            for (int i = 0; i < 16; i++)
            {
                var sprite = NewSprite($"{folder}_corner_{Convert.ToString(i, 2).PadLeft(4, '0')}");
                bySlot[(byte)i] = sprite;
                rs.EditorSetSlot((Corner16Slot)i, new[] { sprite });
            }
            return (rs, bySlot);
        }

        // ── Independent oracle — re-derived from CornerMask's own doc comment,
        // never calls into BitmaskCalculator. ──────────────────────────────────

        private static string TerrainAt(IReadOnlyDictionary<Vector2Int, string> grid, Vector2Int cell)
            => grid.TryGetValue(cell, out var t) ? t : null;

        private static bool BlockIsSecondary(IReadOnlyDictionary<Vector2Int, string> grid, Vector2Int cell,
            Vector2Int vertical, Vector2Int horizontal, string secondary, string center)
        {
            int count = center == secondary ? 1 : 0;
            if (TerrainAt(grid, cell + vertical) == secondary) count++;
            if (TerrainAt(grid, cell + horizontal) == secondary) count++;
            if (TerrainAt(grid, cell + vertical + horizontal) == secondary) count++;
            if (count >= 3) return true;
            if (count <= 1) return false;
            return center == secondary; // exact 2-2 tie: broken by the cell's own terrain
        }

        private static byte ExpectedCornerMask(IReadOnlyDictionary<Vector2Int, string> grid, Vector2Int cell, string secondary)
        {
            string center = TerrainAt(grid, cell);
            byte mask = 0;
            if (BlockIsSecondary(grid, cell, Vector2Int.up, Vector2Int.left, secondary, center)) mask |= 0b1000;    // NW
            if (BlockIsSecondary(grid, cell, Vector2Int.up, Vector2Int.right, secondary, center)) mask |= 0b0100;   // NE
            if (BlockIsSecondary(grid, cell, Vector2Int.down, Vector2Int.right, secondary, center)) mask |= 0b0010; // SE
            if (BlockIsSecondary(grid, cell, Vector2Int.down, Vector2Int.left, secondary, center)) mask |= 0b0001;  // SW
            return mask;
        }

        private static void AssertRoundTripsEverywhere(
            Dictionary<Vector2Int, string> grid, TilesetRuleset ruleset, Dictionary<byte, Sprite> spriteBySlot,
            int xMin, int xMax, int yMin, int yMax, HashSet<byte> masksSeen)
        {
            for (int x = xMin; x <= xMax; x++)
            for (int y = yMin; y <= yMax; y++)
            {
                var cell = new Vector2Int(x, y);
                byte expectedMask = ExpectedCornerMask(grid, cell, Secondary);

                // The production mask function must agree with the independently
                // re-derived spec at every single cell.
                byte actualMask = BitmaskCalculator.CornerMask(grid, cell, Secondary);
                Assert.AreEqual(expectedMask, actualMask,
                    $"cell {cell}: BitmaskCalculator.CornerMask disagrees with the documented corner-majority convention.");

                var resolved = TerrainTileResolver.ResolveVariantForCell(ruleset, grid, cell, Primary, 0);
                Assert.AreSame(spriteBySlot[expectedMask], resolved,
                    $"cell {cell}: resolved sprite must be the one tagged for corner signature " +
                    $"{Convert.ToString(expectedMask, 2).PadLeft(4, '0')}, matching what the grid itself dictates.");

                masksSeen.Add(expectedMask);
            }
        }

        [Test]
        public void CrossShapedIsland_EveryInteriorCell_SpriteMatchesGridCornerSignature()
        {
            var grid = new Dictionary<Vector2Int, string>();
            for (int x = 0; x <= 8; x++)
            for (int y = 0; y <= 8; y++)
                grid[new Vector2Int(x, y)] = (x == 4 || y == 4) ? Secondary : Primary;

            var (ruleset, spriteBySlot) = NewTaggedCornerRuleset("cross_pack");
            var masksSeen = new HashSet<byte>();

            AssertRoundTripsEverywhere(grid, ruleset, spriteBySlot, 1, 7, 1, 7, masksSeen);

            Assert.GreaterOrEqual(masksSeen.Count, 4,
                "Sanity: a cross-shaped island must exercise more than one corner signature.");
        }

        [Test]
        public void DiagonalWatershed_EveryInteriorCell_SpriteMatchesGridCornerSignature_IncludingTieBreakCells()
        {
            // The exact scenario the Corner16 model exists for: a diagonal cut is
            // legitimately half one material, half the other along the border,
            // while every CORNER still reads unambiguously (or resolves the 2-2
            // tie deterministically via the painted cell's own terrain).
            var grid = new Dictionary<Vector2Int, string>();
            for (int x = 0; x <= 8; x++)
            for (int y = 0; y <= 8; y++)
                grid[new Vector2Int(x, y)] = (y > x) ? Secondary : Primary;

            var (ruleset, spriteBySlot) = NewTaggedCornerRuleset("diagonal_pack");
            var masksSeen = new HashSet<byte>();

            AssertRoundTripsEverywhere(grid, ruleset, spriteBySlot, 1, 7, 1, 7, masksSeen);

            Assert.IsTrue(masksSeen.Contains(0b1101),
                "Sanity: the diagonal watershed must exercise at least one genuine 2-2 tie-break cell " +
                "(signature 1101 = CornerNWNESW), not just clean majority corners.");
        }

        // ── Item 6: grid-boundary safety ────────────────────────────────────

        [Test]
        public void CellAtGridBoundary_MissingNeighborsExcluded_DoesNotThrow()
        {
            // Only a 3x3 patch of the world is known at all; every cell outside it
            // is simply absent from the dictionary (not "primary", not
            // "secondary" — unknown). The corner cell (0,0) has 5 of its 8
            // neighbours off-grid.
            var grid = new Dictionary<Vector2Int, string>();
            for (int x = 0; x <= 2; x++)
            for (int y = 0; y <= 2; y++)
                grid[new Vector2Int(x, y)] = Secondary;

            var (ruleset, spriteBySlot) = NewTaggedCornerRuleset("boundary_pack");

            Sprite resolved = null;
            Assert.DoesNotThrow(() =>
                resolved = TerrainTileResolver.ResolveVariantForCell(ruleset, grid, new Vector2Int(0, 0), Primary, 0));

            byte expectedMask = ExpectedCornerMask(grid, new Vector2Int(0, 0), Secondary);
            Assert.AreSame(spriteBySlot[expectedMask], resolved);
        }

        [Test]
        public void CellFarOutsideAnyKnownData_ResolvesToCornerNone_DoesNotThrow()
        {
            var grid = new Dictionary<Vector2Int, string> { { new Vector2Int(0, 0), Primary } };
            var (ruleset, spriteBySlot) = NewTaggedCornerRuleset("far_pack");

            Sprite resolved = null;
            Assert.DoesNotThrow(() =>
                resolved = TerrainTileResolver.ResolveVariantForCell(ruleset, grid, new Vector2Int(500, -500), Primary, 0));

            Assert.AreSame(spriteBySlot[0b0000], resolved,
                "A cell with no recorded terrain at all, and no neighbours, must resolve to CornerNone, not throw.");
        }
    }
}
