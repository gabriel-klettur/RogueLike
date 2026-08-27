using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// EditMode tests for the damage-mitigation seam added to <see cref="Health"/>:
    /// flat defense, per-element resistance, and the post-hit grace window. See
    /// <c>Health.MitigateDamage</c> / <c>Health.ApplyDamage</c> for the formulas these
    /// pin.
    ///
    /// <c>Time.time</c> does not advance inside an EditMode test (same caveat as
    /// <c>ChatBubbleTests</c> / <c>AttackStateSwingTests</c>), which is exactly why the
    /// grace window defaults to 0 (disabled) on a freshly-created <see cref="Health"/> —
    /// every test in the repository that calls <c>TakeDamage</c> more than once on the
    /// same instance predates this feature and never opts into a nonzero window. The
    /// grace tests below opt in explicitly via <see cref="Health.SetPostHitGrace"/>.
    /// </summary>
    [TestFixture]
    public class HealthMitigationTests
    {
        private Health CreateHealth(int maxHp = 100)
        {
            var go = new GameObject("MitigationTestEntity");
            var h = go.AddComponent<Health>();
            h.Initialize(maxHp);
            return h;
        }

        private static void Destroy(Health h)
        {
            if (h != null) Object.DestroyImmediate(h.gameObject);
        }

        // ── Defense ──────────────────────────────────────────────────────

        [Test]
        public void Defense_ReducesDamage_ByFlatAmount()
        {
            var h = CreateHealth(100);
            try
            {
                h.SetDefense(5);
                h.TakeDamage(20, null, null);
                Assert.AreEqual(85, h.CurrentHp, "20 raw - 5 defense = 15 damage taken.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void Defense_NeverDropsAHitBelowTheFloor()
        {
            var h = CreateHealth(100);
            try
            {
                // Defense (10) exceeds the raw hit (3). An uncapped subtraction would be
                // negative (no damage, or worse, healing); MinDamageAfterDefense guarantees
                // a landed hit is never a complete no-op.
                h.SetDefense(10);
                h.TakeDamage(3, null, null);
                Assert.AreEqual(99, h.CurrentHp,
                    "A hit that survives to MitigateDamage with any damage remaining must " +
                    "always deal at least 1.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void Defense_ZeroIsTheDefault_UnmitigatedBehaviourUnchanged()
        {
            var h = CreateHealth(100);
            try
            {
                h.TakeDamage(30, null, null);
                Assert.AreEqual(70, h.CurrentHp,
                    "A Health with no SetDefense call must subtract raw damage exactly as " +
                    "every monster shipped before this field existed.");
            }
            finally { Destroy(h); }
        }

        // ── Elemental resistance ────────────────────────────────────────

        [Test]
        public void ResistedElement_TakesLessDamage_ThanTheSameHitUnresisted()
        {
            var resisted = CreateHealth(1000);
            var unresisted = CreateHealth(1000);
            try
            {
                resisted.SetResistances(new[]
                {
                    new ElementResistance { element = SpellElement.Ice, multiplier = 0.25f },
                });

                resisted.TakeDamage(20, null, SpellElement.Ice);
                unresisted.TakeDamage(20, null, SpellElement.Ice);

                int resistedLoss = 1000 - resisted.CurrentHp;
                int unresistedLoss = 1000 - unresisted.CurrentHp;

                Assert.Less(resistedLoss, unresistedLoss,
                    "The entity with an Ice resistance entry must take less Ice damage " +
                    "than an identical entity with none.");
                Assert.AreEqual(5, resistedLoss, "round(20 * 0.25) = 5, defense 0.");
                Assert.AreEqual(20, unresistedLoss, "No entry for the element -> multiplier 1.0.");
            }
            finally { Destroy(resisted); Destroy(unresisted); }
        }

        [Test]
        public void UnlistedElement_TakesFullDamage()
        {
            var h = CreateHealth(100);
            try
            {
                // Fire resistance authored; the hit is Ice. Only the listed element changes.
                h.SetResistances(new[]
                {
                    new ElementResistance { element = SpellElement.Fire, multiplier = 0.1f },
                });
                h.TakeDamage(20, null, SpellElement.Ice);
                Assert.AreEqual(80, h.CurrentHp,
                    "An element with no table entry must default to a 1.0 multiplier.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void ZeroMultiplier_IsTrueImmunity_AndBypassesTheDefenseFloor()
        {
            var h = CreateHealth(100);
            try
            {
                h.SetDefense(0);
                h.SetResistances(new[]
                {
                    new ElementResistance { element = SpellElement.Dark, multiplier = 0f },
                });
                h.TakeDamage(999, null, SpellElement.Dark);
                Assert.AreEqual(100, h.CurrentHp,
                    "A 0.0 multiplier must zero the hit entirely — unlike defense, which " +
                    "can only ever shave a landed hit down to the floor, never to zero.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void NoElement_IgnoresResistanceTable_LikeMeleeDamage()
        {
            var h = CreateHealth(100);
            try
            {
                // A physical (no-element) hit must not consult the resistance table at all,
                // even if every entry in it would otherwise zero the hit out.
                h.SetResistances(new[]
                {
                    new ElementResistance { element = SpellElement.Fire, multiplier = 0f },
                    new ElementResistance { element = SpellElement.Ice, multiplier = 0f },
                });
                h.TakeDamage(15, null, null);
                Assert.AreEqual(85, h.CurrentHp);
            }
            finally { Destroy(h); }
        }

        // ── Post-hit grace window ───────────────────────────────────────

        [Test]
        public void PostHitGrace_DefaultsToZero_NeverBlocksAnything()
        {
            var h = CreateHealth(100);
            try
            {
                h.TakeDamage(10, null, null);
                h.TakeDamage(10, null, null);
                Assert.AreEqual(80, h.CurrentHp,
                    "A Health that never calls SetPostHitGrace must let every attributed " +
                    "hit through, exactly like before this field existed — this is what " +
                    "keeps the entire pre-existing EditMode suite green.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void PostHitGrace_BlocksASecondAttributedHit_WithinTheWindow()
        {
            var h = CreateHealth(100);
            try
            {
                h.SetPostHitGrace(0.5f);
                h.TakeDamage(10, null, null);
                Assert.AreEqual(90, h.CurrentHp, "precondition: the first hit lands.");

                // Second independent attacker landing in the same instant — Time.time is
                // frozen in EditMode, which for this one purpose is exactly the scenario
                // the window exists to catch (several hits with no time between them).
                h.TakeDamage(10, null, null);
                Assert.AreEqual(90, h.CurrentHp,
                    "A second attributed hit inside the grace window must be refused " +
                    "outright — this is what stops a pack of N attackers from each " +
                    "landing a full hit on the same tick.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void PostHitGrace_DoesNotBlockADotTick()
        {
            var h = CreateHealth(100);
            try
            {
                h.SetPostHitGrace(0.5f);
                h.TakeDamage(10, null, null);
                Assert.AreEqual(90, h.CurrentHp, "precondition: the attributed hit lands.");

                // A DoT/zone tick landing inside the same window must NOT be eaten —
                // otherwise a melee swing landing in the same frame as a scheduled Burn
                // tick would silently stop the burn.
                h.TakeDotDamage(4, null, null);
                Assert.AreEqual(86, h.CurrentHp,
                    "TakeDotDamage must ignore the post-hit grace window entirely.");

                // And the DoT tick must not have armed/extended the window either — a
                // further attributed hit issued right after should still be refused by
                // the ORIGINAL window, not a fresh one started by the tick.
                h.TakeDamage(10, null, null);
                Assert.AreEqual(86, h.CurrentHp,
                    "A DoT tick must not itself arm the grace window.");
            }
            finally { Destroy(h); }
        }

        [Test]
        public void PostHitGrace_IsPerInstance_NotGlobal()
        {
            var a = CreateHealth(100);
            var b = CreateHealth(100);
            try
            {
                a.SetPostHitGrace(0.5f);
                b.SetPostHitGrace(0.5f);

                a.TakeDamage(10, null, null);
                // b has taken no hit yet, so its window must not be affected by a's.
                b.TakeDamage(10, null, null);

                Assert.AreEqual(90, a.CurrentHp);
                Assert.AreEqual(90, b.CurrentHp,
                    "Each Health owns its own grace timer; one entity's hit must never " +
                    "gate another's.");
            }
            finally { Destroy(a); Destroy(b); }
        }
    }
}
