using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Gameplay.Tile
{
    /// <summary>
    /// Unit tests for <see cref="RulesetSolver"/>.
    ///
    /// Exercises:
    ///   - ComputeSlot: every 4-bit mask maps to the matching <see cref="Blob16Slot"/>.
    ///   - ComputeSlot: upper nibble is masked out (forward-compat with Blob47).
    ///   - ResolveVariant: null ruleset / unassigned slot / single variant / multiple variants.
    ///   - ResolveVariant: deterministic dispatch by hash seed.
    ///   - Resolve (combo helper): same result as the two-step call.
    /// </summary>
    [TestFixture]
    public class RulesetSolverTests
    {
        private static Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            return sprite;
        }

        private static TilesetRuleset NewRuleset(string folder = "test")
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            rs.EditorSetMetadata(folder, "grass", null, 0, AutoTileModel.Blob16);
            return rs;
        }

        // ---------------- ComputeSlot ----------------

        [Test]
        public void ComputeSlot_Zero_ReturnsIsolated()
        {
            Assert.AreEqual(Blob16Slot.Isolated, RulesetSolver.ComputeSlot(0));
        }

        [Test]
        public void ComputeSlot_AllBits_ReturnsCenter()
        {
            Assert.AreEqual(Blob16Slot.Center, RulesetSolver.ComputeSlot(0b1111));
        }

        [Test]
        public void ComputeSlot_BitN_ReturnsConnectN()
        {
            Assert.AreEqual(Blob16Slot.ConnectN, RulesetSolver.ComputeSlot(BitmaskCalculator.BitN));
        }

        [Test]
        public void ComputeSlot_BitE_ReturnsConnectE()
        {
            Assert.AreEqual(Blob16Slot.ConnectE, RulesetSolver.ComputeSlot(BitmaskCalculator.BitE));
        }

        [Test]
        public void ComputeSlot_BitS_ReturnsConnectS()
        {
            Assert.AreEqual(Blob16Slot.ConnectS, RulesetSolver.ComputeSlot(BitmaskCalculator.BitS));
        }

        [Test]
        public void ComputeSlot_BitW_ReturnsConnectW()
        {
            Assert.AreEqual(Blob16Slot.ConnectW, RulesetSolver.ComputeSlot(BitmaskCalculator.BitW));
        }

        [Test]
        public void ComputeSlot_NS_ReturnsConnectNS()
        {
            byte m = (byte)(BitmaskCalculator.BitN | BitmaskCalculator.BitS);
            Assert.AreEqual(Blob16Slot.ConnectNS, RulesetSolver.ComputeSlot(m));
        }

        [Test]
        public void ComputeSlot_EW_ReturnsConnectEW()
        {
            byte m = (byte)(BitmaskCalculator.BitE | BitmaskCalculator.BitW);
            Assert.AreEqual(Blob16Slot.ConnectEW, RulesetSolver.ComputeSlot(m));
        }

        [Test]
        public void ComputeSlot_AllSixteenMasks_RoundTrip()
        {
            for (int i = 0; i < 16; i++)
            {
                var slot = RulesetSolver.ComputeSlot((byte)i);
                Assert.AreEqual((Blob16Slot)i, slot, $"Mask {i:X1} should round-trip to enum value {(Blob16Slot)i}.");
            }
        }

        [Test]
        public void ComputeSlot_UpperNibble_IsIgnored()
        {
            // Forward-compat: Blob47 will use the high nibble for inner-corner bits.
            Assert.AreEqual(Blob16Slot.Isolated, RulesetSolver.ComputeSlot(0xF0));
            Assert.AreEqual(Blob16Slot.Center,   RulesetSolver.ComputeSlot(0xFF));
        }

        // ---------------- ResolveVariant ----------------

        [Test]
        public void ResolveVariant_NullRuleset_ReturnsNull()
        {
            Assert.IsNull(RulesetSolver.ResolveVariant(null, Blob16Slot.Center, 0));
        }

        [Test]
        public void ResolveVariant_UnassignedSlot_ReturnsNull()
        {
            var rs = NewRuleset();
            try
            {
                Assert.IsNull(RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 0));
            }
            finally { Object.DestroyImmediate(rs); }
        }

        [Test]
        public void ResolveVariant_SingleVariant_ReturnsThatVariant()
        {
            var rs = NewRuleset();
            var s = NewSprite("only");
            try
            {
                rs.EditorSetSlot(Blob16Slot.Center, new[] { s });
                var result = RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 12345);
                Assert.AreSame(s, result);
            }
            finally
            {
                Object.DestroyImmediate(rs);
                Object.DestroyImmediate(s.texture);
                Object.DestroyImmediate(s);
            }
        }

        [Test]
        public void ResolveVariant_DeterministicByHashSeed()
        {
            var rs = NewRuleset();
            var a = NewSprite("a");
            var b = NewSprite("b");
            var c = NewSprite("c");
            try
            {
                rs.EditorSetSlot(Blob16Slot.Center, new[] { a, b, c });
                var first = RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 7);
                var second = RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 7);
                Assert.AreSame(first, second, "Same seed must always pick the same variant.");
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in new[] { a, b, c })
                {
                    Object.DestroyImmediate(s.texture);
                    Object.DestroyImmediate(s);
                }
            }
        }

        [Test]
        public void ResolveVariant_DifferentSeeds_DistributeAcrossVariants()
        {
            var rs = NewRuleset();
            var a = NewSprite("a");
            var b = NewSprite("b");
            var c = NewSprite("c");
            try
            {
                rs.EditorSetSlot(Blob16Slot.Center, new[] { a, b, c });
                Assert.AreSame(a, RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 0));
                Assert.AreSame(b, RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 1));
                Assert.AreSame(c, RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 2));
                Assert.AreSame(a, RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, 3), "Index wraps modulo variant count.");
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in new[] { a, b, c })
                {
                    Object.DestroyImmediate(s.texture);
                    Object.DestroyImmediate(s);
                }
            }
        }

        [Test]
        public void ResolveVariant_NegativeSeed_DoesNotThrowAndPicksValidIndex()
        {
            var rs = NewRuleset();
            var a = NewSprite("a");
            var b = NewSprite("b");
            try
            {
                rs.EditorSetSlot(Blob16Slot.Center, new[] { a, b });
                var result = RulesetSolver.ResolveVariant(rs, Blob16Slot.Center, -7);
                Assert.IsTrue(result == a || result == b, "Negative seeds must still produce a valid variant.");
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in new[] { a, b })
                {
                    Object.DestroyImmediate(s.texture);
                    Object.DestroyImmediate(s);
                }
            }
        }

        // ---------------- Resolve (combo helper) ----------------

        [Test]
        public void Resolve_CombinesComputeSlotAndResolveVariant()
        {
            var rs = NewRuleset();
            var sprite = NewSprite("center");
            try
            {
                rs.EditorSetSlot(Blob16Slot.Center, new[] { sprite });
                var result = RulesetSolver.Resolve(rs, 0b1111, 0);
                Assert.AreSame(sprite, result);
            }
            finally
            {
                Object.DestroyImmediate(rs);
                Object.DestroyImmediate(sprite.texture);
                Object.DestroyImmediate(sprite);
            }
        }

        // ---------------- TilesetRuleset.IsComplete ----------------

        [Test]
        public void IsComplete_Empty_ReturnsFalse()
        {
            var rs = NewRuleset();
            try
            {
                Assert.IsFalse(rs.IsComplete());
            }
            finally { Object.DestroyImmediate(rs); }
        }

        [Test]
        public void IsComplete_AllSixteenSlotsAssigned_ReturnsTrue()
        {
            var rs = NewRuleset();
            var sprites = new Sprite[16];
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Blob16Slot)i, new[] { sprites[i] });
                }
                Assert.IsTrue(rs.IsComplete());
            }
            finally
            {
                Object.DestroyImmediate(rs);
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null) continue;
                    Object.DestroyImmediate(sprites[i].texture);
                    Object.DestroyImmediate(sprites[i]);
                }
            }
        }

        [Test]
        public void IsComplete_MissingOneSlot_ReturnsFalse()
        {
            var rs = NewRuleset();
            var sprites = new Sprite[15];
            try
            {
                for (int i = 0; i < 15; i++) // Skip Center (slot 15)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Blob16Slot)i, new[] { sprites[i] });
                }
                Assert.IsFalse(rs.IsComplete());
            }
            finally
            {
                Object.DestroyImmediate(rs);
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null) continue;
                    Object.DestroyImmediate(sprites[i].texture);
                    Object.DestroyImmediate(sprites[i]);
                }
            }
        }

        [Test]
        public void IsComplete_Blob47Model_AlwaysReturnsFalse()
        {
            var rs = NewRuleset();
            var sprites = new Sprite[16];
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Blob16Slot)i, new[] { sprites[i] });
                }
                rs.EditorSetMetadata("test", "grass", null, 0, AutoTileModel.Blob47);
                Assert.IsFalse(rs.IsComplete(), "Blob47 model is reserved for v2 — IsComplete must return false.");
            }
            finally
            {
                Object.DestroyImmediate(rs);
                for (int i = 0; i < sprites.Length; i++)
                {
                    if (sprites[i] == null) continue;
                    Object.DestroyImmediate(sprites[i].texture);
                    Object.DestroyImmediate(sprites[i]);
                }
            }
        }
    }
}
