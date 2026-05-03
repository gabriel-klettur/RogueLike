using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// Phase 2.6 acceptance: <see cref="ProceduralWorldFactory"/> turns a
    /// designer-authored <see cref="WorldDescriptor"/> into a
    /// <see cref="IChunkProvider"/> + <see cref="DictionaryTileIdTable"/>
    /// that paint the right tiles for the configured biome — without the
    /// factory ever reaching into Unity's scene graph or the asset
    /// database. These tests are the plug-and-play parity proof for the
    /// factory: same inputs in, same chunks out.
    /// </summary>
    [TestFixture]
    public class ProceduralWorldFactoryTests
    {
        // ── Helpers ─────────────────────────────────────────────────────────────

        private static WorldConfig MakeConfig(string slug, int chunkSize, long seed)
        {
            var cfg = ScriptableObject.CreateInstance<WorldConfig>();
            SetField(cfg, "dimensionSlug", slug);
            SetField(cfg, "chunkSize",     chunkSize);
            SetField(cfg, "tileSize",      1f);
            SetField(cfg, "seed",          seed);
            cfg.name = $"WorldConfig:{slug}";
            return cfg;
        }

        private static WorldDescriptor MakeDescriptor(
            string slug,
            ProceduralBiomeKind biomeKind,
            string primaryTile,
            string secondaryTile,
            float noiseThreshold,
            int chunkSize,
            long seed,
            bool useStreaming = true)
        {
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            SetField(d, "slug",              slug);
            SetField(d, "displayName",       slug);
            SetField(d, "config",            MakeConfig(slug, chunkSize, seed));
            SetField(d, "useChunkStreaming", useStreaming);
            SetField(d, "activeRadius",      1);
            SetField(d, "biomeKind",         biomeKind);
            SetField(d, "primaryTile",       primaryTile);
            SetField(d, "secondaryTile",     secondaryTile);
            SetField(d, "noiseThreshold",    noiseThreshold);
            d.name = $"Descriptor:{slug}";
            return d;
        }

        private static void DestroyDescriptor(WorldDescriptor d)
        {
            if (d == null) return;
            if (d.Config != null) UnityEngine.Object.DestroyImmediate(d.Config);
            UnityEngine.Object.DestroyImmediate(d);
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = obj.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {obj.GetType().Name}.");
            f.SetValue(obj, value);
        }

        // ── Behaviour ───────────────────────────────────────────────────────────

        [Test]
        public void Build_UniformBiome_ProvidesProviderThatPaintsPrimaryTileEverywhere()
        {
            var d = MakeDescriptor("procuni", ProceduralBiomeKind.Uniform,
                                   primaryTile: "stone", secondaryTile: "",
                                   noiseThreshold: 0f, chunkSize: 4, seed: 7L);
            try
            {
                var w = ProceduralWorldFactory.Build(d);
                Assert.IsNotNull(w.Provider, "Factory must return a non-null provider.");
                Assert.IsNotNull(w.Tiles,    "Factory must return the tile-id table it registered.");
                Assert.AreEqual(4, w.ChunkSize);
                Assert.AreEqual(1, w.LayerCount);

                ushort stoneId = w.Tiles.GetId("stone");
                Assert.AreNotEqual(0, stoneId,
                    "Primary tile must be registered with a non-zero id.");

                var chunk = w.Provider.Get(new ChunkCoord(d.Id, 0, 0));
                Assert.IsNotNull(chunk, "Provider must yield a baseline chunk.");
                for (int y = 0; y < w.ChunkSize; y++)
                for (int x = 0; x < w.ChunkSize; x++)
                    Assert.AreEqual(stoneId, chunk.Get(0, x, y),
                        $"Uniform biome must paint primary tile at every cell ({x},{y}).");
            }
            finally { DestroyDescriptor(d); }
        }

        [Test]
        public void Build_NoiseSplitBiome_RegistersBothTilesAndProducesBothAcrossChunks()
        {
            var d = MakeDescriptor("procns", ProceduralBiomeKind.NoiseSplit,
                                   primaryTile: "grass", secondaryTile: "dirt",
                                   noiseThreshold: 0.5f, chunkSize: 8, seed: 42L);
            try
            {
                var w = ProceduralWorldFactory.Build(d);
                ushort grassId = w.Tiles.GetId("grass");
                ushort dirtId  = w.Tiles.GetId("dirt");
                Assert.AreNotEqual(0, grassId);
                Assert.AreNotEqual(0, dirtId);
                Assert.AreNotEqual(grassId, dirtId,
                    "Two distinct tile names must hash to two distinct ids.");

                // Sweep a 4x4 patch of chunks. With frequency 0.1 and threshold
                // 0.5 a single small chunk near the origin can fall entirely in
                // one band of the noise — the biome is correct, the sample is
                // just narrow. Sweeping multiple chunks proves the biome paints
                // both bands across the grid without depending on a particular
                // seed-vs-band coincidence.
                int grassCount = 0, dirtCount = 0;
                for (int cx = -2; cx < 2; cx++)
                for (int cy = -2; cy < 2; cy++)
                {
                    var chunk = w.Provider.Get(new ChunkCoord(d.Id, cx, cy));
                    for (int y = 0; y < w.ChunkSize; y++)
                    for (int x = 0; x < w.ChunkSize; x++)
                    {
                        ushort id = chunk.Get(0, x, y);
                        if      (id == grassId) grassCount++;
                        else if (id == dirtId)  dirtCount++;
                        else Assert.Fail($"Cell ({x},{y}) of chunk ({cx},{cy}) " +
                                         $"holds unexpected id {id}.");
                    }
                }

                int total = 16 * w.ChunkSize * w.ChunkSize;
                Assert.AreEqual(total, grassCount + dirtCount,
                    "Every cell across the swept patch must hold one of the two configured tiles.");
                Assert.Greater(grassCount, 0,
                    "NoiseSplit must paint some primary tiles somewhere in the swept patch.");
                Assert.Greater(dirtCount, 0,
                    "NoiseSplit must paint some secondary tiles somewhere in the swept patch.");
            }
            finally { DestroyDescriptor(d); }
        }

        [Test]
        public void Build_DeterministicAcrossInvocations_SameDescriptorYieldsSameChunkBytes()
        {
            // Two descriptors that match on slug/seed/biome must produce
            // byte-identical chunks. Phase 4 networking depends on this.
            var a = MakeDescriptor("procdet", ProceduralBiomeKind.NoiseSplit,
                                   "grass", "dirt", 0.5f, chunkSize: 4, seed: 99L);
            var b = MakeDescriptor("procdet", ProceduralBiomeKind.NoiseSplit,
                                   "grass", "dirt", 0.5f, chunkSize: 4, seed: 99L);
            try
            {
                var wa = ProceduralWorldFactory.Build(a);
                var wb = ProceduralWorldFactory.Build(b);

                var coord = new ChunkCoord(a.Id, 1, -2);
                var ca = wa.Provider.Get(coord);
                var cb = wb.Provider.Get(new ChunkCoord(b.Id, 1, -2));
                Assert.AreEqual(ca.ComputeCrc32(), cb.ComputeCrc32(),
                    "Same descriptor inputs must yield identical procedural baselines.");
            }
            finally { DestroyDescriptor(a); DestroyDescriptor(b); }
        }

        [Test]
        public void Build_NullDescriptor_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ProceduralWorldFactory.Build(null));
        }

        [Test]
        public void Build_DescriptorWithoutConfig_Throws()
        {
            var d = ScriptableObject.CreateInstance<WorldDescriptor>();
            try
            {
                Assert.Throws<InvalidOperationException>(() => ProceduralWorldFactory.Build(d),
                    "Factory must refuse a descriptor with no WorldConfig.");
            }
            finally { UnityEngine.Object.DestroyImmediate(d); }
        }

        [Test]
        public void Build_DescriptorWithBiomeNone_Throws()
        {
            var d = MakeDescriptor("procnone", ProceduralBiomeKind.None,
                                   "grass", "dirt", 0.5f, chunkSize: 4, seed: 1L);
            try
            {
                Assert.Throws<InvalidOperationException>(() => ProceduralWorldFactory.Build(d),
                    "Factory must refuse a descriptor whose biome kind is None — " +
                    "such worlds are hand-crafted, not streamed.");
            }
            finally { DestroyDescriptor(d); }
        }

        [Test]
        public void Build_HonoursDescriptorChunkSizeFromConfig()
        {
            var d = MakeDescriptor("procsize", ProceduralBiomeKind.Uniform,
                                   "stone", "", 0f, chunkSize: 16, seed: 5L);
            try
            {
                var w = ProceduralWorldFactory.Build(d);
                Assert.AreEqual(16, w.ChunkSize);
                var chunk = w.Provider.Get(new ChunkCoord(d.Id, 0, 0));
                Assert.AreEqual(16, chunk.Size,
                    "Provider chunks must adopt the descriptor's chunk size.");
            }
            finally { DestroyDescriptor(d); }
        }
    }
}
