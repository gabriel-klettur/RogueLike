using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Combat
{
    /// <summary>
    /// The shipped monsters, checked for the kinds of wrong that are SILENT in game.
    ///
    /// <para>Every rule below was a live defect when this fixture was written, and none of
    /// them errored, warned or failed a test:</para>
    /// <list type="bullet">
    /// <item><c>mon1</c> was entirely zero — <c>hp: 0</c>, <c>speed: 0</c>,
    /// <c>aggroRange: 0</c>, <c>meleeDamage: 0</c> — and <c>assignments.json</c> mapped it to
    /// <c>Monster_Default</c>, so it spawned dead-on-arrival and could perceive nothing.</item>
    /// <item><c>barbol_oscuro</c> had <c>speed: 10</c> against <c>chasingSpeed: 2.25</c>: it
    /// patrolled four times faster than it chased, and <c>FleeState</c> multiplies the WALK
    /// speed, so panicking sent it across half a screen per second.</item>
    /// <item><c>useAttackTelegraph</c> was authored on two monsters and nine of eleven
    /// hostiles wound up for 0 s, which is below <c>AttackState</c>'s telegraph floor — so
    /// the field promised a tell that could not be drawn.</item>
    /// </list>
    ///
    /// <para>The rules are STRUCTURAL, never a table of expected numbers: encounter tuning
    /// belongs to whoever is balancing the game, and a fixture that pinned the values would
    /// go red on every honest edit. What it pins is that a monster is playable at all.</para>
    /// </summary>
    public class ShippedMonsterDataSanityTests
    {
        /// <summary>
        /// Mirrors <c>AttackState.MinWindupToTelegraph</c>. Below this the tell and the hit
        /// are indistinguishable in time, so the telegraph is skipped.
        /// </summary>
        private const float MinWindupToTelegraph = 0.15f;

        private static List<MonsterDefinition> _monsters;

        [OneTimeSetUp]
        public void LoadOnce()
        {
            // Loaded once per fixture, not per test: AssetDatabase.FindAssets over the whole
            // project is the expensive part, and this corpus does not change mid-run.
            _monsters = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null && !string.IsNullOrEmpty(m.monsterKey))
                .ToList();
        }

        /// <summary>
        /// A monster that can chase somebody. Vendors and quest NPCs deliberately author
        /// <c>aggroRange: 0</c> and <c>chasingSpeed: 0</c> — being harmless is their whole
        /// job — so the hostile rules must not be applied to them.
        /// </summary>
        private static bool IsHostile(MonsterDefinition m)
            => m.stats.aggroRange > 0f && m.stats.chasingSpeed > 0f;

        private static void AssertAll(System.Func<MonsterDefinition, string> check)
        {
            Assert.IsNotEmpty(_monsters, "No MonsterDefinition assets found — every " +
                                         "assertion below would pass vacuously.");

            var failures = new StringBuilder();
            foreach (var m in _monsters)
            {
                string problem = check(m);
                if (!string.IsNullOrEmpty(problem))
                    failures.Append("  ").Append(m.monsterKey).Append(": ").Append(problem).Append('\n');
            }

            if (failures.Length > 0) Assert.Fail("\n" + failures.ToString().TrimEnd());
        }

        // ── Alive at all ─────────────────────────────────────────────────────────

        [Test]
        public void EveryMonster_HasPositiveHp()
        {
            AssertAll(m => m.stats.hp > 0 ? null
                : $"hp is {m.stats.hp}. It spawns dead: Health.IsDead is currentHp <= 0, so " +
                  "the FSM enters UnconsciousState on its first tick.");
        }

        [Test]
        public void EveryMonster_HasANonNegativeCooldown()
        {
            AssertAll(m => m.stats.meleeCooldown >= 0f ? null
                : $"meleeCooldown is {m.stats.meleeCooldown}");
        }

        // ── Hostiles specifically ────────────────────────────────────────────────

        [Test]
        public void EveryHostile_CanReachWhatItChases()
        {
            AssertAll(m => !IsHostile(m) || m.stats.meleeRange > 0f ? null
                : "aggroRange and chasingSpeed say it hunts, but meleeRange is 0 — " +
                  "ChaseState can never hand over to AttackState, so it closes to zero " +
                  "distance and stands there.");
        }

        [Test]
        public void EveryHostile_DealsDamage()
        {
            AssertAll(m => !IsHostile(m) || m.stats.meleeDamage > 0 || m.autoCast ? null
                : "it chases and swings for 0 damage, and does not cast either.");
        }

        [Test]
        public void NoHostile_PatrolsFasterThanItChases()
        {
            // The one that shipped: barbol_oscuro at speed 10 against chasingSpeed 2.25.
            // It is also the flee speed — FleeState multiplies `speed`, not `chasingSpeed` —
            // so the same field decides how fast a panicking monster crosses the screen.
            AssertAll(m => !IsHostile(m) || m.stats.speed <= m.stats.chasingSpeed ? null
                : $"speed {m.stats.speed} > chasingSpeed {m.stats.chasingSpeed}. A monster " +
                  "that patrols faster than it pursues reads as broken, and `speed` is also " +
                  "the base FleeState multiplies.");
        }

        [Test]
        public void NoHostile_HasAMeleeRangeBeyondItsAggroRange()
        {
            AssertAll(m => !IsHostile(m) || m.stats.meleeRange <= m.stats.aggroRange ? null
                : $"meleeRange {m.stats.meleeRange} exceeds aggroRange {m.stats.aggroRange}: " +
                  "it can hit further than it can notice.");
        }

        // ── Fields that promise something ────────────────────────────────────────

        [Test]
        public void ATelegraphIsOnlyAuthored_WhereItCanActuallyBeDrawn()
        {
            AssertAll(m => !m.useAttackTelegraph ||
                           m.stats.attackWindupSeconds >= MinWindupToTelegraph ? null
                : $"useAttackTelegraph is on with attackWindupSeconds " +
                  $"{m.stats.attackWindupSeconds}, below AttackState's {MinWindupToTelegraph} " +
                  "floor — the tell is skipped, so the field reads as a promise the game " +
                  "does not keep.");
        }

        [Test]
        public void AStandoffIsNeverShorterThanTheMonstersOwnReach()
        {
            AssertAll(m => m.aiTuning.desiredRange <= 0f ||
                           m.aiTuning.desiredRange > m.stats.meleeRange ? null
                : $"desiredRange {m.aiTuning.desiredRange} is inside meleeRange " +
                  $"{m.stats.meleeRange}: the monster would hold a standoff at a distance " +
                  "from which ChaseState immediately hands over to AttackState.");
        }

        [Test]
        public void AStandoffFitsInsideTheAggroRing()
        {
            AssertAll(m => m.aiTuning.desiredRange <= 0f ||
                           m.aiTuning.desiredRange < m.stats.aggroRange ? null
                : $"desiredRange {m.aiTuning.desiredRange} is outside aggroRange " +
                  $"{m.stats.aggroRange}: backing off to it drops the target.");
        }

        [Test]
        public void ALeashIsNeverShorterThanTheAggroRing()
        {
            AssertAll(m => m.aiTuning.leashRange <= 0f ||
                           m.aiTuning.leashRange > m.stats.aggroRange ? null
                : $"leashRange {m.aiTuning.leashRange} is inside aggroRange " +
                  $"{m.stats.aggroRange}: it would give up before it had gone anywhere.");
        }
    }
}
