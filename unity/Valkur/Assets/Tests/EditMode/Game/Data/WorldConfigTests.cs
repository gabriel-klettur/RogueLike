using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Pins WorldConfig defaults and the deterministic slug→Guid mapping that
    /// underpins WorldId stability across editor sessions and across machines.
    /// </summary>
    [TestFixture]
    public class WorldConfigTests
    {
        [Test]
        public void LegacyChunkSize_IsFifty()
        {
            // Phase 0 invariant: the published constant matches the historical
            // tile dimension so existing scenes continue to load identically.
            Assert.AreEqual(50, WorldConfig.LegacyChunkSize);
        }

        [Test]
        public void LegacyFallback_HasLegacyChunkSize()
        {
            var cfg = WorldConfig.CreateLegacyFallback();
            try
            {
                Assert.AreEqual(WorldConfig.LegacyChunkSize, cfg.ChunkSize);
                Assert.AreEqual("base", cfg.DimensionSlug);
                Assert.AreEqual(1f, cfg.TileSize);
            }
            finally { Object.DestroyImmediate(cfg); }
        }

        [Test]
        public void Id_IsDeterministicForSlug()
        {
            var a = WorldConfig.CreateLegacyFallback();
            var b = WorldConfig.CreateLegacyFallback();
            try
            {
                Assert.AreEqual(a.Id.Value, b.Id.Value,
                    "Same slug must produce the same WorldId.Value across instances " +
                    "so save files are portable across machines.");
                Assert.AreEqual(a.Id.Slug, b.Id.Slug);
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ChunkSize_IsAtLeastOne()
        {
            // Defensive: even with a zero in the inspector, runtime never
            // returns less than 1 so divisions don't blow up.
            var cfg = ScriptableObject.CreateInstance<WorldConfig>();
            try
            {
                // chunkSize default is LegacyChunkSize so this is already > 0;
                // we mainly assert the documented contract.
                Assert.GreaterOrEqual(cfg.ChunkSize, 1);
            }
            finally { Object.DestroyImmediate(cfg); }
        }
    }
}
