using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Core.Coordinates;
using Valkur.Data;
using Valkur.Data.Chunks;
using Valkur.Gameplay.World.Chunks;

namespace Valkur.Tests.EditMode.Game.World.Chunks
{
    /// <summary>
    /// Phase 2.6 acceptance for the ProceduralWorld.asset shipped with the
    /// project: the asset must (a) exist where the editor utility writes it,
    /// (b) have chunk streaming opted in, and (c) feed a working procedural
    /// pipeline through <see cref="ProceduralWorldFactory"/> — the same path
    /// <c>GameplaySceneSetup.EnsureProceduralChunkStreamer</c> takes at boot.
    ///
    /// If this regresses, the menu utility and the factory have drifted
    /// apart and the bootstrap will fail silently in scenes that select
    /// the procedural world.
    /// </summary>
    [TestFixture]
    public class ProceduralWorldAssetEndToEndTests
    {
        private const string DescPath = "Assets/_Project/Data/Worlds/ProceduralWorld.asset";

        [Test]
        public void Asset_Exists_AndIsWellFormed()
        {
            var d = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescPath);
            Assert.IsNotNull(d,
                $"ProceduralWorld descriptor must exist at {DescPath}. " +
                "Run 'Valkur/World/Create or Refresh Procedural World Assets' to (re)build it.");

            Assert.AreEqual("proc_demo", d.Slug);
            Assert.IsNotNull(d.Config, "Procedural world must reference a WorldConfig.");
            Assert.AreEqual(32, d.Config.ChunkSize, "Phase 2 canonical chunk size is 32.");
            Assert.IsTrue(d.UseChunkStreaming, "Procedural world must opt into chunk streaming.");
            Assert.AreNotEqual(ProceduralBiomeKind.None, d.BiomeKind,
                "Procedural world must declare a non-None biome kind.");
            Assert.IsFalse(string.IsNullOrEmpty(d.PrimaryTile),
                "Procedural world must declare a primary tile name.");
        }

        [Test]
        public void Factory_BuildsAStreamablePipelineFromTheAsset()
        {
            var d = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescPath);
            if (d == null)
                Assert.Inconclusive($"Asset missing at {DescPath}; cannot exercise factory.");

            var w = ProceduralWorldFactory.Build(d);
            Assert.IsNotNull(w.Provider, "Factory must produce a provider.");
            Assert.IsNotNull(w.Tiles,    "Factory must produce a tile-id table.");
            Assert.AreEqual(d.Config.ChunkSize, w.ChunkSize,
                "Streamed-world chunk size must match the descriptor's WorldConfig.");

            // Pull a chunk: it must be fully populated with valid ids
            // (the biome contract: every cell painted, no zero ids).
            var chunk = w.Provider.Get(new ChunkCoord(d.Id, 0, 0));
            Assert.IsNotNull(chunk, "Provider must yield a baseline chunk for (0,0).");

            int cells = w.ChunkSize * w.ChunkSize;
            int painted = 0;
            for (int y = 0; y < w.ChunkSize; y++)
            for (int x = 0; x < w.ChunkSize; x++)
                if (chunk.Get(0, x, y) != 0) painted++;

            Assert.AreEqual(cells, painted,
                "Every cell of the procedural baseline must hold a non-zero tile id — " +
                "an empty cell would mean the biome failed to populate part of the chunk.");
        }

        [Test]
        public void TwoChunks_Differ_NoiseSplitProducesPattern()
        {
            var d = AssetDatabase.LoadAssetAtPath<WorldDescriptor>(DescPath);
            if (d == null)
                Assert.Inconclusive($"Asset missing at {DescPath}; cannot exercise factory.");
            if (d.BiomeKind != ProceduralBiomeKind.NoiseSplit)
                Assert.Pass("Asset's biome is not NoiseSplit — no pattern check needed.");

            var w = ProceduralWorldFactory.Build(d);
            var a = w.Provider.Get(new ChunkCoord(d.Id, 0, 0));
            var b = w.Provider.Get(new ChunkCoord(d.Id, 5, 5));

            Assert.AreNotEqual(a.ComputeCrc32(), b.ComputeCrc32(),
                "Two distant chunks of a NoiseSplit biome must differ — " +
                "if they match, the noise channel is collapsed and the world looks uniform.");
        }
    }
}
