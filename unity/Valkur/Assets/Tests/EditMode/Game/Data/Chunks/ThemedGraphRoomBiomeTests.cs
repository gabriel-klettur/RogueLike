using NUnit.Framework;
using Valkur.Core.Coordinates;
using Valkur.Data.Chunks;

namespace Valkur.Tests.EditMode.Game.Data.Chunks
{
    /// <summary>
    /// Pins <see cref="ThemedGraphRoomBiome"/>: themes assigned per region,
    /// determinism per (seed, region), region boundaries produce theme
    /// transitions, single-theme arrays produce uniform output, and the
    /// constructor rejects an empty theme array.
    /// </summary>
    [TestFixture]
    public class ThemedGraphRoomBiomeTests
    {
        private const int Size = 8;
        private const int SupercellTiles = 16;
        private const long Seed = 31337L;

        private static ThemedGraphRoomBiome.ThemePalette Theme(string id, string floor, string wall)
        {
            return new ThemedGraphRoomBiome.ThemePalette
            {
                id = id,
                floorTile = floor,
                wallTile  = wall,
            };
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void Constructor_EmptyThemeArray_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new ThemedGraphRoomBiome("empty", themes: System.Array.Empty<ThemedGraphRoomBiome.ThemePalette>()),
                "Empty themes array must be rejected — without palettes the painter has nothing to do.");
        }

        [Test]
        public void Theme_DeterministicPerRegion()
        {
            var themes = new[]
            {
                Theme("forest", "grass", "tree"),
                Theme("ice",    "snow",  "icewall"),
                Theme("hell",   "magma", "obsidian"),
            };
            var biome = new ThemedGraphRoomBiome("multi", themes,
                supercellTiles: SupercellTiles, regionSizeSupercells: 4);

            for (int i = 0; i < 5; i++)
            {
                var first  = biome.ResolveTheme(7, 11, Seed);
                var second = biome.ResolveTheme(7, 11, Seed);
                Assert.AreEqual(first.id, second.id,
                    "Same seed + supercell must always resolve to the same theme.");
            }
        }

        [Test]
        public void Themes_VaryAcrossRegions()
        {
            var themes = new[]
            {
                Theme("a", "fa", "wa"),
                Theme("b", "fb", "wb"),
                Theme("c", "fc", "wc"),
                Theme("d", "fd", "wd"),
            };
            var biome = new ThemedGraphRoomBiome("multi", themes,
                supercellTiles: SupercellTiles, regionSizeSupercells: 4);

            // Sweep many regions; with 4 themes we expect at least 2 to
            // appear over a 6x6 region grid.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int rx = 0; rx < 6; rx++)
            for (int ry = 0; ry < 6; ry++)
            {
                // Use the centre supercell of each region so the rx/ry → sx/sy
                // mapping is unambiguous (any supercell in the region resolves
                // to the same theme).
                long sx = rx * 4 + 1;
                long sy = ry * 4 + 1;
                seen.Add(biome.ResolveTheme(sx, sy, Seed).id);
            }

            Assert.GreaterOrEqual(seen.Count, 2,
                "Across 36 regions with 4 themes, at least 2 distinct themes " +
                "must appear; if only 1, the FNV mix is collapsing.");
        }

        [Test]
        public void SingleTheme_ProducesUniformOutput()
        {
            var themes = new[] { Theme("only", "f", "w") };
            var biome = new ThemedGraphRoomBiome("solo", themes,
                supercellTiles: SupercellTiles, regionSizeSupercells: 4);

            for (long sx = -3; sx < 3; sx++)
            for (long sy = -3; sy < 3; sy++)
                Assert.AreEqual("only", biome.ResolveTheme(sx, sy, Seed).id,
                    "Single-theme array means every supercell must resolve to the same theme.");
        }

        [Test]
        public void GenerateChunk_PaintsWithRegionTheme()
        {
            // Two themes; verify the chunk at (0,0) paints with whichever
            // theme region (0,0) resolves to.
            var themes = new[]
            {
                Theme("forest", "grass", "tree"),
                Theme("ice",    "snow",  "icewall"),
            };
            var biome = new ThemedGraphRoomBiome("multi", themes,
                supercellTiles: SupercellTiles, regionSizeSupercells: 4,
                roomProbabilityPerMille: 1000);

            var tiles = new DictionaryTileIdTable();
            tiles.Register("grass");   tiles.Register("tree");
            tiles.Register("snow");    tiles.Register("icewall");

            var coord = new ChunkCoord(WorldId.Base, 0, 0);
            var ctx = new BiomeContext(Seed, coord, Size, layerCount: 1, tiles);
            var chunk = biome.GenerateChunk(coord, Seed, ctx);

            // Resolve which theme region (0,0)'s supercell falls into.
            var picked = biome.ResolveTheme(0, 0, Seed);
            ushort floorId = tiles.GetId(picked.floorTile);

            // Pick an interior cell: (3,3). With room prob 1000 this is a
            // floor cell of whichever theme is active for region (0,0).
            Assert.AreEqual(floorId, chunk.Get(0, 3, 3),
                "Interior chunk cell must paint with the resolved theme's floor tile.");
        }
    }
}
