using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Spells
{
    /// <summary>
    /// The arithmetic behind the three new projectile mechanics and the charge.
    ///
    /// <para>Pure functions, tested without a scene, because the failure modes here are
    /// arithmetic rather than wiring: a fan that is not centred on the aim, and a charge
    /// scalar that reads 0 for a spell which does not charge — which would make every
    /// existing projectile in the game deal its minimum damage the moment the field was
    /// added, silently, and only for the spells nobody thought to re-test.</para>
    /// </summary>
    public class SpellMechanicsMathTests
    {
        // ── The volley fan ───────────────────────────────────────────────────

        [Test]
        public void ASingleShot_IsReturnedUntouched()
        {
            var aim = new Vector2(0.6f, 0.8f);

            Assert.AreEqual(aim, ProjectileExecutor.FanDirection(aim, 0, 1, 0f),
                "A spell that never authored the volley fields must fly exactly where it " +
                "always did, including through the floating-point path -- which a lerp over " +
                "a degenerate range would not guarantee.");
            Assert.AreEqual(aim, ProjectileExecutor.FanDirection(aim, 0, 1, 40f));
            Assert.AreEqual(aim, ProjectileExecutor.FanDirection(aim, 0, 5, 0f),
                "Five shots at zero spread is one heading, five times.");
        }

        [Test]
        public void AFan_IsCentredOnTheAim()
        {
            var aim = Vector2.right;
            const int shots = 5;
            const float spread = 40f;

            var first = ProjectileExecutor.FanDirection(aim, 0, shots, spread);
            var middle = ProjectileExecutor.FanDirection(aim, 2, shots, spread);
            var last = ProjectileExecutor.FanDirection(aim, shots - 1, shots, spread);

            Assert.AreEqual(0f, Vector2.SignedAngle(aim, middle), 0.001f,
                "The middle shot of an odd fan flies straight down the aim.");
            Assert.AreEqual(-spread * 0.5f, Vector2.SignedAngle(aim, first), 0.001f);
            Assert.AreEqual(spread * 0.5f, Vector2.SignedAngle(aim, last), 0.001f);
        }

        [Test]
        public void AFan_SpansExactlyTheAuthoredAngle()
        {
            var aim = new Vector2(-0.3f, 0.9f).normalized;
            const int shots = 4;
            const float spread = 46f;

            var first = ProjectileExecutor.FanDirection(aim, 0, shots, spread);
            var last = ProjectileExecutor.FanDirection(aim, shots - 1, shots, spread);

            Assert.AreEqual(spread, Mathf.Abs(Vector2.SignedAngle(first, last)), 0.001f);
        }

        [Test]
        public void AFan_PreservesMagnitude()
        {
            var aim = Vector2.right;
            for (int i = 0; i < 5; i++)
            {
                var d = ProjectileExecutor.FanDirection(aim, i, 5, 60f);
                Assert.AreEqual(1f, d.magnitude, 0.001f,
                    "A rotation must not change the speed of the shot.");
            }
        }

        // ── The charge ───────────────────────────────────────────────────────

        private static SpellDefinition Chargeable()
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = "test_charge";
            s.chargeMaxSeconds = 1.6f;
            s.chargeMinFraction = 0.45f;
            s.chargeDamageMultiplier = 2.6f;
            s.chargeScaleMultiplier = 2.0f;
            return s;
        }

        private static SpellDefinition Plain()
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey = "test_plain";
            return s;
        }

        [Test]
        public void ANonChargeableSpell_IsNeutralAtEveryFraction()
        {
            var s = Plain();
            try
            {
                Assert.IsFalse(s.IsChargeable);
                foreach (float f in new[] { 0f, 0.5f, 1f })
                {
                    Assert.AreEqual(1f, ChargeMath.DamageMultiplier(s, f), 0.0001f,
                        "This is the whole reason ChargeMath exists rather than the " +
                        "multiplication being inlined: a struct field defaults to 0, so an " +
                        "executor reading ctx.ChargeFraction raw would have made every " +
                        "existing projectile deal its minimum damage.");
                    Assert.AreEqual(1f, ChargeMath.ScaleMultiplier(s, f), 0.0001f);
                    Assert.AreEqual(1f, ChargeMath.Resolve(s, f), 0.0001f);
                }
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void ANullSpell_IsNeutralToo()
        {
            Assert.AreEqual(1f, ChargeMath.DamageMultiplier(null, 1f), 0.0001f);
            Assert.AreEqual(1f, ChargeMath.ScaleMultiplier(null, 1f), 0.0001f);
            Assert.IsFalse(ChargeMath.IsFullyCharged(null, 1f));
        }

        [Test]
        public void ASnapCast_LandsAtTheAuthoredMinimumAndAFullHoldAtTheTop()
        {
            var s = Chargeable();
            try
            {
                Assert.AreEqual(0.45f, ChargeMath.Resolve(s, 0f), 0.0001f);
                Assert.AreEqual(1f, ChargeMath.Resolve(s, 1f), 0.0001f);

                // Damage runs 1x at the floor of the ramp up to the authored top, so a snap
                // cast is a WEAKER fireball rather than a free one.
                float snap = ChargeMath.DamageMultiplier(s, 0f);
                float full = ChargeMath.DamageMultiplier(s, 1f);
                Assert.Less(snap, full);
                Assert.AreEqual(2.6f, full, 0.0001f);
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void HoldingPastFull_AddsNothing()
        {
            var s = Chargeable();
            try
            {
                Assert.AreEqual(ChargeMath.DamageMultiplier(s, 1f),
                                ChargeMath.DamageMultiplier(s, 4f), 0.0001f,
                    "Overholding must be inert, and the rig must look inert too, or the " +
                    "player is never told to let go.");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void DamageAndScale_AreSeparateDials()
        {
            var s = Chargeable();
            try
            {
                // 2.6 against 2.0 at full charge. Folding them into one curve would cost the
                // spell the "small and sharp versus big and slow" reading that makes charging
                // interesting rather than just bigger.
                Assert.AreNotEqual(ChargeMath.DamageMultiplier(s, 1f),
                                   ChargeMath.ScaleMultiplier(s, 1f));
            }
            finally { Object.DestroyImmediate(s); }
        }

        [Test]
        public void FullChargeThreshold_IsShortOfOneSoARealHoldCanReachIt()
        {
            var s = Chargeable();
            try
            {
                Assert.Less(ChargeMath.FULL_CHARGE_THRESHOLD, 1f,
                    "A threshold of exactly 1 is unreachable in practice: the release lands " +
                    "on whatever frame the key came up, so the payoff would almost never fire.");
                Assert.IsTrue(ChargeMath.IsFullyCharged(s, 1f));
                Assert.IsFalse(ChargeMath.IsFullyCharged(s, 0.5f));
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
