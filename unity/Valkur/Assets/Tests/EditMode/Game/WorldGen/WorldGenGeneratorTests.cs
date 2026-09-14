using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data.WorldGen;

namespace Valkur.Tests.EditMode.Game.WorldGen
{
    /// <summary>
    /// The world generator's contract: determinism, the biome vocabulary, and the guarantees each
    /// dial in the Seed World editor promises. Phase 1 of <c>.github/SEED_WORLD_ROADMAP.md</c>.
    /// </summary>
    public class WorldGenGeneratorTests
    {
        private const int PreviewSide = 96;

        private static WorldGenSettings Settings(int seed = 1337)
            => new WorldGenSettings { seed = seed, widthTiles = 256, heightTiles = 256 };

        // ── The vocabulary ─────────────────────────────────────────────────────

        /// <summary>
        /// The table is indexed by the enum value, so an entry out of order silently gives one
        /// biome another biome's rules and colour.
        /// </summary>
        [Test]
        public void TheBiomeTable_HasOneEntryPerBiome_InEnumOrder()
        {
            var values = System.Enum.GetValues(typeof(WorldBiome)).Cast<WorldBiome>().ToArray();
            Assert.AreEqual(values.Length, WorldBiomeTable.Count);
            for (int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(i, (int)values[i], "The enum must be contiguous from 0.");
                Assert.AreEqual(values[i], WorldBiomeTable.GetAt(i).Biome, $"Table row {i} is out of order.");
                Assert.IsFalse(string.IsNullOrEmpty(WorldBiomeTable.GetAt(i).DisplayName));
                Assert.AreEqual(255, WorldBiomeTable.GetAt(i).PreviewColor.a, "Preview colours are opaque.");
            }
        }

        /// <summary>Two biomes one colour would be one biome in the preview and its legend.</summary>
        [Test]
        public void EveryBiome_HasADistinctPreviewColour()
        {
            var colours = Enumerable.Range(0, WorldBiomeTable.Count)
                .Select(i => (Color32)WorldBiomeTable.GetAt(i).PreviewColor)
                .Select(c => (c.r << 16) | (c.g << 8) | c.b)
                .ToList();
            Assert.AreEqual(colours.Count, colours.Distinct().Count());
        }

        // ── Determinism ────────────────────────────────────────────────────────

        [Test]
        public void SameSeed_SameSettings_ProducesTheSameWorld()
        {
            var a = WorldGenMap.Generate(Settings(42), PreviewSide);
            var b = WorldGenMap.Generate(Settings(42), PreviewSide);

            CollectionAssert.AreEqual(a.Biomes, b.Biomes);
            CollectionAssert.AreEqual(a.Elevation, b.Elevation);
            Assert.AreEqual(a.SpawnTile, b.SpawnTile);
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentWorlds()
        {
            var a = WorldGenMap.Generate(Settings(1), PreviewSide);
            var b = WorldGenMap.Generate(Settings(2), PreviewSide);

            int differing = 0;
            for (int i = 0; i < a.Biomes.Length; i++)
                if (a.Biomes[i] != b.Biomes[i]) differing++;
            Assert.Greater(differing, a.Biomes.Length / 10, "Two seeds should not be near-identical worlds.");
        }

        /// <summary>
        /// A text seed must never go through <c>string.GetHashCode</c>, which .NET may vary per
        /// process. <see cref="TheHash_IsFnv1a"/> pins the algorithm, so a change of hash is a red
        /// test rather than a silent new world for every shared seed.
        /// </summary>
        [Test]
        public void TextSeeds_HashStably_AndNumbersAreUsedAsIs()
        {
            Assert.IsTrue(WorldSeed.TryParse("1337", out int number));
            Assert.AreEqual(1337, number);

            Assert.IsTrue(WorldSeed.TryParse("valkur", out int a));
            Assert.IsTrue(WorldSeed.TryParse("  valkur ", out int b));
            Assert.AreEqual(a, b, "Surrounding whitespace is not part of a seed.");
            Assert.AreEqual(WorldSeed.Hash("valkur"), a);
            Assert.AreNotEqual(WorldSeed.Hash("valkur"), WorldSeed.Hash("valkyr"));

            Assert.IsFalse(WorldSeed.TryParse("   ", out _));
        }

        [Test]
        public void TheHash_IsFnv1a()
        {
            // FNV-1a of "a": offset basis 2166136261 ^ 'a', times the FNV prime, as a uint.
            uint expected = unchecked((2166136261u ^ 'a') * 16777619u);
            Assert.AreEqual(unchecked((int)expected), WorldSeed.Hash("a"));
        }

        // ── The preview IS the generator ───────────────────────────────────────

        /// <summary>
        /// Every preview cell must be exactly what <see cref="WorldClimate.BiomeAt"/> answers at
        /// that cell's centre. A preview computed any other way would show a world the build
        /// does not produce.
        /// </summary>
        [Test]
        public void EveryPreviewCell_IsTheClimateAnswerAtItsCentre()
        {
            var climate = new WorldClimate(Settings(7));
            var map = WorldGenMap.Generate(climate, PreviewSide);

            for (int row = 0; row < map.Rows; row += 7)
                for (int col = 0; col < map.Columns; col += 7)
                {
                    var p = map.CellCentre(col, row);
                    Assert.AreEqual(climate.BiomeAt(p.x, p.y), map.BiomeOf(col, row), $"cell ({col},{row})");
                }
        }

        [Test]
        public void ThePreview_NeverExceedsItsSide_AndCoversTheWholeWorld()
        {
            var s = Settings();
            s.widthTiles = 2048;
            s.heightTiles = 300;
            var map = WorldGenMap.Generate(s, 320);

            Assert.LessOrEqual(Mathf.Max(map.Columns, map.Rows), 320);
            Assert.GreaterOrEqual(map.Columns * map.TilesPerCell, s.widthTiles - 0.01f);
            Assert.GreaterOrEqual(map.Rows * map.TilesPerCell, s.heightTiles - 0.01f);
        }

        // ── Dials ──────────────────────────────────────────────────────────────

        [Test]
        public void ADisabledBiome_NeverAppears()
        {
            foreach (var biome in new[] { WorldBiome.Plains, WorldBiome.Ocean, WorldBiome.Mountain, WorldBiome.DeepOcean })
            {
                var s = Settings(11);
                s.WeightOf(biome).enabled = false;
                var map = WorldGenMap.Generate(s, PreviewSide);
                Assert.AreEqual(0, map.BiomeCounts[(int)biome], $"{biome} was disabled and still appeared.");
            }
        }

        [Test]
        public void EveryLandBiomeDisabled_StillProducesAWorld()
        {
            var s = Settings(3);
            for (int i = 0; i < WorldBiomeTable.Count; i++)
                if (WorldBiomeTable.GetAt(i).Kind == WorldBiomeKind.Land)
                    s.WeightOf((WorldBiome)i).enabled = false;

            var map = WorldGenMap.Generate(s, PreviewSide);
            Assert.AreEqual(map.CellCount, map.BiomeCounts.Sum());
        }

        [Test]
        public void RaisingTheSeaLevel_NeverShrinksTheWater()
        {
            float previous = -1f;
            foreach (float sea in new[] { 0.2f, 0.4f, 0.6f, 0.8f })
            {
                var s = Settings(5);
                s.seaLevel = sea;
                s.mountainLevel = 1f;
                var map = WorldGenMap.Generate(s, PreviewSide);
                float water = map.Fraction(WorldBiome.Ocean) + map.Fraction(WorldBiome.DeepOcean);
                Assert.GreaterOrEqual(water, previous, $"sea level {sea}");
                previous = water;
            }
        }

        [Test]
        public void FullEdgeFalloff_SinksEveryEdgeCell()
        {
            var s = Settings(9);
            s.edgeFalloff = 1f;
            var climate = new WorldClimate(s);

            for (float x = 0f; x <= s.widthTiles; x += 16f)
            {
                Assert.AreEqual(0f, climate.Sample(x, 0f).Elevation, 1e-4f);
                Assert.AreEqual(0f, climate.Sample(x, s.heightTiles).Elevation, 1e-4f);
            }
        }

        /// <summary>Latitude means the north is colder: compare mean temperature of the two halves.</summary>
        [Test]
        public void Latitude_MakesTheNorthColder()
        {
            var s = Settings(21);
            s.latitude = 1f;
            var map = WorldGenMap.Generate(s, PreviewSide);

            float south = 0f, north = 0f;
            int half = map.Rows / 2;
            for (int row = 0; row < map.Rows; row++)
                for (int col = 0; col < map.Columns; col++)
                {
                    float t = map.Temperature[map.Index(col, row)];
                    if (row < half) south += t; else north += t;
                }
            Assert.Less(north, south);
        }

        [Test]
        public void ZeroRarity_ProducesNoRareBiome()
        {
            var s = Settings(4);
            s.rarity = 0f;
            var map = WorldGenMap.Generate(s, PreviewSide);
            Assert.AreEqual(0, map.BiomeCounts[(int)WorldBiome.Enchanted]);
            Assert.AreEqual(0, map.BiomeCounts[(int)WorldBiome.Corrupted]);
        }

        [Test]
        public void TheSpawn_IsOnWalkableLand()
        {
            var map = WorldGenMap.Generate(Settings(1337), PreviewSide);
            Assert.IsTrue(map.HasSpawn);

            var biome = map.Climate.BiomeAt(map.SpawnTile.x, map.SpawnTile.y);
            var kind = WorldBiomeTable.Get(biome).Kind;
            Assert.That(kind, Is.EqualTo(WorldBiomeKind.Land).Or.EqualTo(WorldBiomeKind.Shore).Or.EqualTo(WorldBiomeKind.Rare),
                $"spawn landed on {biome}");
        }

        // ── Settings ───────────────────────────────────────────────────────────

        /// <summary>
        /// The height cap is the Y-sort budget. A world taller than it would wrap sorting orders
        /// with nothing logged but the clamp warning.
        /// </summary>
        [Test]
        public void TheHeightCap_FitsTheYSortBudget()
        {
            Assert.LessOrEqual(WorldGenSettings.MaxHeightTiles, SortingConfig.MAX_SAFE_WORLD_Y * 2);

            var s = new WorldGenSettings { heightTiles = 100000, widthTiles = 1, mountainLevel = 0f, seaLevel = 0.9f };
            s.Clamp();
            Assert.AreEqual(WorldGenSettings.MaxHeightTiles, s.heightTiles);
            Assert.AreEqual(WorldGenSettings.MinSizeTiles, s.widthTiles);
            Assert.GreaterOrEqual(s.mountainLevel, s.seaLevel, "The mountain line may never sit under the sea.");
        }

        [Test]
        public void Settings_RoundTripThroughJson()
        {
            var s = Settings(99);
            s.WeightOf(WorldBiome.Desert).enabled = false;
            s.WeightOf(WorldBiome.Forest).weight = 2.5f;

            var back = WorldGenSettings.FromJson(s.ToJson());
            Assert.IsNotNull(back);
            Assert.AreEqual(s.ToJson(), back.ToJson());
        }

        /// <summary>A document written before a biome was appended still describes every biome.</summary>
        [Test]
        public void ADocumentMissingBiomes_GainsThem_EnabledAtWeightOne()
        {
            var back = WorldGenSettings.FromJson("{\"seed\":5,\"biomes\":[{\"biome\":4,\"enabled\":false,\"weight\":2}]}");
            Assert.IsNotNull(back);
            Assert.AreEqual(WorldBiomeTable.Count, back.biomes.Count);
            Assert.IsFalse(back.WeightOf(WorldBiome.Forest).enabled);
            Assert.AreEqual(2f, back.WeightOf(WorldBiome.Forest).weight);
            Assert.IsTrue(back.WeightOf(WorldBiome.Snow).enabled);
            Assert.AreEqual(1f, back.WeightOf(WorldBiome.Snow).weight);
        }

        [Test]
        public void MalformedJson_IsRefused_NotHalfRead()
        {
            Assert.IsNull(WorldGenSettings.FromJson(""));
            Assert.IsNull(WorldGenSettings.FromJson(null));
            Assert.IsNull(WorldGenSettings.FromJson("{ this is not json"));
        }

        // ── Noise ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The contrast stretch is what makes the 0..1 dials mean something. Without it fBm sits
        /// around 0.5 and a sea level of 0.3 would drown almost nothing.
        /// </summary>
        [Test]
        public void FractalNoise_CoversMostOfTheUnitRange()
        {
            var noise = new FractalNoise2D(123, 5);
            var rng = new System.Random(1);
            var values = new float[8000];
            for (int i = 0; i < values.Length; i++)
                values[i] = noise.Sample((float)(rng.NextDouble() * 300.0), (float)(rng.NextDouble() * 300.0));
            System.Array.Sort(values);

            float p5 = values[values.Length / 20];
            float p95 = values[values.Length * 19 / 20];
            Assert.Less(p5, 0.25f);
            Assert.Greater(p95, 0.75f);
            Assert.GreaterOrEqual(values[0], 0f);
            Assert.LessOrEqual(values[values.Length - 1], 1f);
        }
    }
}
