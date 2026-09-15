using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Data.Combat
{
    /// <summary>
    /// The two axes a boss phase and an ambient NPC are graded on, and the shape of the knobs
    /// that move them.
    ///
    /// <para><b>A PHASE USED TO CHANGE ONLY WHAT THE BOSS CAST AND WHAT MUSIC PLAYED.</b> Its
    /// movement, its spacing, its reactions and the company it kept were identical from the
    /// first phase to the last — so a three-phase boss was one fight with three spell lists
    /// over it. And a boss with no adds is a duel, which is the single encounter shape where
    /// none of the group layer does anything at all: no shout, no ring, no threat contest, no
    /// reason to look away.</para>
    ///
    /// <para>These are structural rather than behavioural, deliberately. Both features are
    /// reached through <c>BossPhaseController</c>'s phase-change event, which needs a live
    /// scene, a <c>MonsterSpawner</c> and a catalog — so what is pinned here is that the knobs
    /// exist, that they are NEUTRAL when unauthored (which is what makes them safe to land on a
    /// shipped boss), and that the shipped data has not quietly authored one of them.</para>
    /// </summary>
    public class BossPhaseAndAmbientTests
    {
        // ── Boss phases ──────────────────────────────────────────────────────────

        [Test]
        public void APhaseCanBringAdds()
        {
            var phase = new BossDefinition.Phase();
            Assert.IsNotNull(phase.adds,
                "An unauthored list must be empty, not null — BossConfigurator iterates it.");
            Assert.IsEmpty(phase.adds,
                "Every phase shipped before this field summons nothing, and must keep doing so.");
        }

        [Test]
        public void AnAddDefaultsToSomethingSpawnable()
        {
            // A default of count 0 or radius 0 would make an authored add do nothing, or drop
            // the whole group on the boss's own tile — both read as the feature being broken
            // rather than as the defaults being wrong.
            var add = new BossDefinition.AddSpawn();
            Assert.That(add.count, Is.GreaterThanOrEqualTo(1));
            Assert.That(add.spawnRadius, Is.GreaterThan(0f));
            Assert.AreEqual(0, add.levelBonus);
        }

        [Test]
        public void PhaseAiKnobsAreNeutralUnlessAuthored()
        {
            // Zero means "keep whatever the boss's own aiTuning says". BossConfigurator only
            // publishes a knob the phase actually set, which is the same contract
            // FSMMonsterBrain.PublishBehaviourTuning uses and the reason a shipped boss is
            // untouched by this whole feature.
            var phase = new BossDefinition.Phase();
            Assert.AreEqual(0f, phase.desiredRange);
            Assert.AreEqual(0f, phase.chaseSpeedMultiplier);
            Assert.AreEqual(0f, phase.dodgeChance);
        }

        [Test]
        public void EveryShippedBossPhaseIsStillNeutral()
        {
            // The shipped bosses were authored before any of this existed. If one of them has
            // picked up a value, it happened by accident — and a boss whose phase 2 silently
            // halves its standoff is a balance change nobody wrote down.
            var bosses = AssetDatabase.FindAssets("t:BossDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<BossDefinition>)
                .Where(b => b != null)
                .ToList();

            Assert.IsNotEmpty(bosses, "No BossDefinition assets found — this test is vacuous.");

            foreach (var boss in bosses)
                foreach (var phase in boss.phases)
                {
                    Assert.IsEmpty(phase.adds ?? System.Array.Empty<BossDefinition.AddSpawn>(),
                        $"{boss.name} phase '{phase.label}' summons adds that nobody authored.");
                    Assert.AreEqual(0f, phase.desiredRange, $"{boss.name} '{phase.label}'");
                    Assert.AreEqual(0f, phase.chaseSpeedMultiplier, $"{boss.name} '{phase.label}'");
                    Assert.AreEqual(0f, phase.dodgeChance, $"{boss.name} '{phase.label}'");
                }
        }

        [Test]
        public void AnyAddNamedByAPhaseMustExist()
        {
            // BossConfigurator skips an unresolved key with a warning and summons the rest, so a
            // renamed monster costs a phase its minions and leaves the fight looking merely easy.
            var known = AssetDatabase.FindAssets("t:MonsterDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MonsterDefinition>)
                .Where(m => m != null)
                .Select(m => m.monsterKey)
                .ToHashSet();

            foreach (var boss in AssetDatabase.FindAssets("t:BossDefinition")
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Select(AssetDatabase.LoadAssetAtPath<BossDefinition>)
                         .Where(b => b != null))
                foreach (var phase in boss.phases)
                    foreach (var add in phase.adds ?? System.Array.Empty<BossDefinition.AddSpawn>())
                        Assert.IsTrue(known.Contains(add.monsterKey),
                            $"{boss.name} phase '{phase.label}' summons '{add.monsterKey}', " +
                            "which is not a monsterKey in any shipped definition.");
        }

        // ── Ambient life ─────────────────────────────────────────────────────────

        [Test]
        public void TheStrollSettlesAtNight()
        {
            // Read out of the source rather than driven, because the behaviour needs a live
            // DayNightCycle singleton and a measured animation length — neither of which exists
            // in EditMode, so a driven test would silently take the daytime branch and pass
            // while asserting nothing. What can be checked here is that the night branch is
            // wired at BOTH the places it has to be: the idle length and the wander leash.
            string source = System.IO.File.ReadAllText(
                System.IO.Path.GetDirectoryName(Application.dataPath) +
                "/Assets/_Project/Scripts/Gameplay/Enemies/FSM/States/StrollState.cs");

            Assert.That(source, Does.Contain("NIGHT_WANDER_RADIUS"),
                "A villager who wanders the same ground at midnight as at noon is a prop.");
            Assert.That(source, Does.Contain("_settledForNight = IsNight()"),
                "The clock must be sampled once per BOUT — reading it per frame would stop a " +
                "character mid-step because the sun set.");
            Assert.That(source, Does.Contain("_settledForNight ? NIGHT_IDLE_CYCLES_MIN"),
                "Settling is mostly about standing still, not about a shorter leash.");
            Assert.That(source, Does.Contain("_settledForNight ? NIGHT_WANDER_RADIUS"),
                "and the leash is the other half.");
        }

        [Test]
        public void TheStrollDoesNotSubscribeToTheDayNightClock()
        {
            // DayNightCycle's phase change is a STATIC event and Domain Reload is off, so a
            // state instance that subscribed would keep a destroyed NPC alive for the session —
            // and a state object does not survive a detour through DamageState, so it has
            // nowhere reliable to unsubscribe. Polling per bout is the cheaper correctness.
            string source = System.IO.File.ReadAllText(
                System.IO.Path.GetDirectoryName(Application.dataPath) +
                "/Assets/_Project/Scripts/Gameplay/Enemies/FSM/States/StrollState.cs");

            Assert.That(source, Does.Not.Contain("OnPhaseChanged +="),
                "A static event subscription from a state object is a leak with no teardown.");
        }
    }
}
