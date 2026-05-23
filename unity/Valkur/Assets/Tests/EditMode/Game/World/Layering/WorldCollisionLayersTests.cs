using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.World.Layering;

namespace Valkur.Tests.EditMode.Game.World.Layering
{
    /// <summary>
    /// Unit tests for <see cref="WorldCollisionLayers"/> — the helper that owns
    /// "what bits does an entity on visual layer N need in its
    /// <see cref="Collider2D.includeLayers"/> mask".
    ///
    /// These tests pin the contract that drives the M2 per-visual-layer
    /// collision filtering: a single <c>WorldL{N}</c> slot + the always-on
    /// <c>WorldAll</c> wildcard slot, NOTHING else. A regression here would
    /// silently re-enable collisions across every layer (the very symptom
    /// the system was built to prevent).
    /// </summary>
    [TestFixture]
    public class WorldCollisionLayersTests
    {
        [SetUp]
        public void SetUp()
        {
            // Domain Reload is OFF in this project — the static cache from a
            // previous test (or previous Play session) can survive. Force a
            // re-resolve so every test starts from a clean lookup state.
            WorldCollisionLayers.Invalidate();
        }

        // ── Field discovery ------------------------------------------------

        [Test]
        public void TagManager_DefinesAllNineWorldLayers()
        {
            // If any of WorldL0..WorldL8 is missing the entire collision
            // pipeline silently collapses to "everything collides". Catching
            // it here turns the silent collapse into a loud test failure.
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int layer = WorldCollisionLayers.GetWorldLayerIndex(i);
                Assert.GreaterOrEqual(layer, 0,
                    $"Physics layer 'WorldL{i}' must be defined in TagManager.");
            }
        }

        [Test]
        public void TagManager_DefinesWorldAllLayer()
        {
            Assert.GreaterOrEqual(WorldCollisionLayers.GetWorldAllIndex(), 0,
                "Physics layer 'WorldAll' must be defined in TagManager — " +
                "it is the wildcard slot every entity opts into.");
        }

        // ── IncludeMaskFor contract ----------------------------------------

        [Test]
        public void IncludeMaskFor_Layer0_IncludesWorldL0AndWorldAll()
        {
            int mask = WorldCollisionLayers.IncludeMaskFor(0);
            int worldL0 = WorldCollisionLayers.GetWorldLayerIndex(0);
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();

            Assert.AreNotEqual(0, mask & (1 << worldL0),
                "Layer-0 entity's includeLayers must contain the WorldL0 bit.");
            Assert.AreNotEqual(0, mask & (1 << worldAll),
                "Every entity's includeLayers must contain the WorldAll bit.");
        }

        [Test]
        public void IncludeMaskFor_Layer7_IncludesWorldL7AndWorldAll()
        {
            int mask = WorldCollisionLayers.IncludeMaskFor(7);
            int worldL7 = WorldCollisionLayers.GetWorldLayerIndex(7);
            int worldAll = WorldCollisionLayers.GetWorldAllIndex();

            Assert.AreNotEqual(0, mask & (1 << worldL7),
                "Layer-7 entity's includeLayers must contain the WorldL7 bit.");
            Assert.AreNotEqual(0, mask & (1 << worldAll),
                "Every entity's includeLayers must contain the WorldAll bit.");
        }

        [Test]
        public void IncludeMaskFor_Layer0_ExcludesEveryOtherWorldLayer()
        {
            // THE contract behind the user's question: an entity on visual
            // layer 0 must NOT have the bit for WorldL1..WorldL8 set. If any
            // of those bits leak in, the entity would suddenly collide with
            // colliders painted on those layers — the very bug the filter is
            // meant to prevent.
            int mask = WorldCollisionLayers.IncludeMaskFor(0);
            for (int other = 1; other < WorldCollisionLayers.LayerCount; other++)
            {
                int otherBit = 1 << WorldCollisionLayers.GetWorldLayerIndex(other);
                Assert.AreEqual(0, mask & otherBit,
                    $"includeLayers for visual layer 0 must NOT contain the WorldL{other} bit.");
            }
        }

        [Test]
        public void IncludeMaskFor_Layer7_ExcludesEveryOtherWorldLayer()
        {
            // Symmetric guarantee: an entity that climbed to layer 7 must
            // collide ONLY with cells stamped onto WorldL7 (+ wildcard) and
            // must phase straight through every collider on layers 0..6 + 8.
            int mask = WorldCollisionLayers.IncludeMaskFor(7);
            for (int other = 0; other < WorldCollisionLayers.LayerCount; other++)
            {
                if (other == 7) continue;
                int otherBit = 1 << WorldCollisionLayers.GetWorldLayerIndex(other);
                Assert.AreEqual(0, mask & otherBit,
                    $"includeLayers for visual layer 7 must NOT contain the WorldL{other} bit.");
            }
        }

        [Test]
        public void IncludeMaskFor_EveryLayer_HasExactlyTwoBitsSet()
        {
            // Sanity belt across the whole 0..8 range: every visual layer's
            // mask contains exactly two bits — one WorldL{N} + one WorldAll.
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int mask = WorldCollisionLayers.IncludeMaskFor(i);
                int popcount = CountSetBits(mask);
                Assert.AreEqual(2, popcount,
                    $"IncludeMaskFor({i}) should have exactly 2 bits set " +
                    $"(WorldL{i} + WorldAll); got mask 0x{mask:X} with {popcount} bits.");
            }
        }

        [Test]
        public void IncludeMaskFor_OutOfRange_ReturnsZeroOrWorldAllOnly()
        {
            // The helper degrades gracefully for out-of-range values
            // (GetWorldLayerIndex returns -1, which short-circuits the
            // per-layer bit but still ORs WorldAll). Asserting this so
            // callers can pass a clamped/raw value defensively.
            int worldAllBit = 1 << WorldCollisionLayers.GetWorldAllIndex();
            int below = WorldCollisionLayers.IncludeMaskFor(-1);
            int above = WorldCollisionLayers.IncludeMaskFor(99);

            Assert.AreEqual(worldAllBit, below,
                "IncludeMaskFor(-1) should still include WorldAll (per-layer bit drops).");
            Assert.AreEqual(worldAllBit, above,
                "IncludeMaskFor(99) should still include WorldAll (per-layer bit drops).");
        }

        // ── AllWorldLayersMask --------------------------------------------

        [Test]
        public void AllWorldLayersMask_ContainsEveryWorldLayerAndWorldAll()
        {
            // Used by NPC + Projectile colliders in M2.1 (they collide with
            // every painted cell regardless of tag). If any bit is missing
            // those entities would phase through certain cells.
            int mask = WorldCollisionLayers.AllWorldLayersMask();
            for (int i = 0; i < WorldCollisionLayers.LayerCount; i++)
            {
                int bit = 1 << WorldCollisionLayers.GetWorldLayerIndex(i);
                Assert.AreNotEqual(0, mask & bit,
                    $"AllWorldLayersMask must contain WorldL{i}.");
            }
            int worldAllBit = 1 << WorldCollisionLayers.GetWorldAllIndex();
            Assert.AreNotEqual(0, mask & worldAllBit,
                "AllWorldLayersMask must contain WorldAll.");
        }

        // ── Helpers --------------------------------------------------------

        private static int CountSetBits(int v)
        {
            int n = 0;
            while (v != 0) { n += v & 1; v = (int)((uint)v >> 1); }
            return n;
        }
    }
}
