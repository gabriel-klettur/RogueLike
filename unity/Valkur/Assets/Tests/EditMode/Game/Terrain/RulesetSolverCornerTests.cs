using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.TileEditor;

namespace Valkur.Tests.EditMode.Game.Terrain
{
    /// <summary>
    /// Unit tests for the Corner16 half of <see cref="RulesetSolver"/> —
    /// <see cref="RulesetSolver.ComputeCornerSlot"/> and the
    /// <see cref="Corner16Slot"/> overloads of <see cref="RulesetSolver.ResolveVariant"/>
    /// / <see cref="RulesetSolver.ResolveCorner"/> — plus <see cref="TilesetRuleset.IsComplete"/>'s
    /// Corner16-specific "must have a secondary terrain" requirement. Mirrors
    /// <see cref="RulesetSolverTests"/>'s Blob16 coverage one-for-one so a
    /// regression in either model's solver is caught the same way.
    /// </summary>
    [TestFixture]
    public class RulesetSolverCornerTests
    {
        private static Sprite NewSprite(string name)
        {
            var tex = new Texture2D(1, 1);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            sprite.name = name;
            return sprite;
        }

        private static void DestroySprite(Sprite s)
        {
            if (s == null) return;
            Object.DestroyImmediate(s.texture);
            Object.DestroyImmediate(s);
        }

        private static TilesetRuleset NewCornerRuleset(string folder = "test_corner")
        {
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            rs.EditorSetMetadata(folder, "grass", "dirt", 0, AutoTileModel.Corner16);
            return rs;
        }

        // ---------------- ComputeCornerSlot ----------------

        [Test]
        public void ComputeCornerSlot_AllSixteenMasks_RoundTrip()
        {
            for (int i = 0; i < 16; i++)
            {
                var slot = RulesetSolver.ComputeCornerSlot((byte)i);
                Assert.AreEqual((Corner16Slot)i, slot, $"Mask {i:X1} should round-trip to enum value {(Corner16Slot)i}.");
            }
        }

        [Test]
        public void ComputeCornerSlot_UpperNibble_IsIgnored()
        {
            Assert.AreEqual(Corner16Slot.CornerNone, RulesetSolver.ComputeCornerSlot(0xF0));
            Assert.AreEqual(Corner16Slot.CornerFull, RulesetSolver.ComputeCornerSlot(0xFF));
        }

        // ---------------- ResolveVariant(Corner16Slot) ----------------

        [Test]
        public void ResolveVariant_NullRuleset_ReturnsNull()
        {
            Assert.IsNull(RulesetSolver.ResolveVariant(null, Corner16Slot.CornerFull, 0));
        }

        [Test]
        public void ResolveVariant_UnassignedSlot_ReturnsNull()
        {
            var rs = NewCornerRuleset();
            try { Assert.IsNull(RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 0)); }
            finally { Object.DestroyImmediate(rs); }
        }

        [Test]
        public void ResolveVariant_SingleVariant_ReturnsThatVariant()
        {
            var rs = NewCornerRuleset();
            var s = NewSprite("only");
            try
            {
                rs.EditorSetSlot(Corner16Slot.CornerFull, new[] { s });
                var result = RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 999);
                Assert.AreSame(s, result);
            }
            finally { Object.DestroyImmediate(rs); DestroySprite(s); }
        }

        [Test]
        public void ResolveVariant_DifferentSeeds_DistributeAcrossVariants()
        {
            var rs = NewCornerRuleset();
            var a = NewSprite("a");
            var b = NewSprite("b");
            var c = NewSprite("c");
            try
            {
                rs.EditorSetSlot(Corner16Slot.CornerFull, new[] { a, b, c });
                Assert.AreSame(a, RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 0));
                Assert.AreSame(b, RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 1));
                Assert.AreSame(c, RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 2));
                Assert.AreSame(a, RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerFull, 3), "Index wraps modulo variant count.");
            }
            finally { Object.DestroyImmediate(rs); DestroySprite(a); DestroySprite(b); DestroySprite(c); }
        }

        [Test]
        public void ResolveVariant_DeterministicByHashSeed()
        {
            var rs = NewCornerRuleset();
            var a = NewSprite("a");
            var b = NewSprite("b");
            try
            {
                rs.EditorSetSlot(Corner16Slot.CornerNW, new[] { a, b });
                var first = RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerNW, 42);
                var second = RulesetSolver.ResolveVariant(rs, Corner16Slot.CornerNW, 42);
                Assert.AreSame(first, second, "Same seed must always pick the same variant.");
            }
            finally { Object.DestroyImmediate(rs); DestroySprite(a); DestroySprite(b); }
        }

        // ---------------- ResolveCorner (combo helper) ----------------

        [Test]
        public void ResolveCorner_CombinesComputeCornerSlotAndResolveVariant()
        {
            var rs = NewCornerRuleset();
            var sprite = NewSprite("full");
            try
            {
                rs.EditorSetSlot(Corner16Slot.CornerFull, new[] { sprite });
                var result = RulesetSolver.ResolveCorner(rs, 0b1111, 0);
                Assert.AreSame(sprite, result);
            }
            finally { Object.DestroyImmediate(rs); DestroySprite(sprite); }
        }

        // ---------------- TilesetRuleset.IsComplete (Corner16) ----------------

        [Test]
        public void IsComplete_Corner16_Empty_ReturnsFalse()
        {
            var rs = NewCornerRuleset();
            try { Assert.IsFalse(rs.IsComplete()); }
            finally { Object.DestroyImmediate(rs); }
        }

        [Test]
        public void IsComplete_Corner16_AllSixteenSlotsAssignedWithSecondary_ReturnsTrue()
        {
            var rs = NewCornerRuleset();
            var sprites = new Sprite[16];
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Corner16Slot)i, new[] { sprites[i] });
                }
                Assert.IsTrue(rs.IsComplete());
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in sprites) DestroySprite(s);
            }
        }

        [Test]
        public void IsComplete_Corner16_AllSixteenSlotsAssignedButNoSecondaryTerrain_ReturnsFalse()
        {
            // The exact guard that makes Corner16 different from Blob16: a
            // fully-populated slot table is not enough — a Corner16 ruleset is BY
            // DEFINITION a two-material transition, and without a secondary
            // terrain the corner-mask calculator has nothing to test corners
            // against, so a fully-populated-but-secondary-less ruleset must still
            // read as incomplete.
            var rs = ScriptableObject.CreateInstance<TilesetRuleset>();
            rs.EditorSetMetadata("test_corner_no_secondary", "grass", null, 0, AutoTileModel.Corner16);
            var sprites = new Sprite[16];
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Corner16Slot)i, new[] { sprites[i] });
                }
                Assert.IsFalse(rs.IsComplete());
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in sprites) DestroySprite(s);
            }
        }

        [Test]
        public void IsComplete_Corner16_MissingOneSlot_ReturnsFalse()
        {
            var rs = NewCornerRuleset();
            var sprites = new Sprite[15];
            try
            {
                for (int i = 0; i < 15; i++) // skip CornerFull (slot 15)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Corner16Slot)i, new[] { sprites[i] });
                }
                Assert.IsFalse(rs.IsComplete());
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in sprites) DestroySprite(s);
            }
        }

        [Test]
        public void IsComplete_Corner16Model_BlobSlotsFilledButCornerSlotsEmpty_ReturnsFalse()
        {
            // The two slot tables (Blob16Slot vs Corner16Slot) are separate
            // storage (TilesetRuleset.slots vs .cornerSlots) — filling the WRONG
            // one while Model == Corner16 must not count. Guards against the two
            // models sharing storage by accident.
            var rs = NewCornerRuleset();
            var sprites = new Sprite[16];
            try
            {
                for (int i = 0; i < 16; i++)
                {
                    sprites[i] = NewSprite($"s{i}");
                    rs.EditorSetSlot((Blob16Slot)i, new[] { sprites[i] });
                }
                Assert.IsFalse(rs.IsComplete(), "Filling Blob16 slots must not satisfy a Corner16 ruleset's completeness check.");
            }
            finally
            {
                Object.DestroyImmediate(rs);
                foreach (var s in sprites) DestroySprite(s);
            }
        }
    }
}
