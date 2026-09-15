using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using Valkur.Data;
using Valkur.Tests.Support;

namespace Valkur.Tests.EditMode.Data.Spells
{
    /// <summary>
    /// The shipped cast phases, and the balance surface that wiring two dead flags created.
    ///
    /// <para><c>allowMovement</c> and <c>interruptible</c> were authored on all 104 spells and
    /// read by NOBODY for the life of the project — CLAUDE.md lists them among the four inert
    /// casting flags. That means no shipped value was ever chosen by watching what it does: they
    /// are defaults and intuitions. Now that they are live, every one of them is balance.</para>
    ///
    /// <para>This fixture is what makes that inheritance visible instead of silent. It does not
    /// claim the values are RIGHT — nobody has played them yet. It claims they are BOUNDED, and
    /// it goes red the day somebody authors a wind-up long enough to feel like a freeze.</para>
    /// </summary>
    [TestFixture]
    [Category(TestCategories.ShippedData)]
    public class ShippedCastPhaseDataTests
    {
        /// <summary>The longest wind-up authored on purpose (summon_wolf). A spell that plants
        /// its caster for longer than this is not a wind-up, it is a stun the player inflicted
        /// on themselves, and it should be a decision somebody takes deliberately.</summary>
        private const float MAX_PLANT_SECONDS = 1.25f;

        private static List<SpellDefinition> LoadShippedSpells()
        {
            var spells = new List<SpellDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:SpellDefinition"))
            {
                var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (spell != null) spells.Add(spell);
            }
            return spells;
        }

        [Test]
        public void TheEightAuthoredWindUps_AreOnDisk()
        {
            var expected = new Dictionary<string, float>
            {
                { "war_cry",        0.90f },
                { "summon_wolf",    1.20f },
                { "raise_thrall",   1.00f },
                { "guardian_light", 0.80f },
                { "blizzard",       0.70f },
                { "thunderclap",    0.55f },
                { "meteor_shower",  0.80f },
                { "charged_bolt",   0.35f },
            };

            var found = new Dictionary<string, float>();
            foreach (var spell in LoadShippedSpells())
                if (expected.ContainsKey(spell.spellKey)) found[spell.spellKey] = spell.prepareDuration;

            Assert.That(found.Count, Is.EqualTo(expected.Count), "every named spell must exist");
            foreach (var pair in expected)
            {
                Assert.That(found[pair.Key], Is.EqualTo(pair.Value).Within(0.001f),
                    $"{pair.Key} is one of the eight heavy spells given a visible wind-up; its " +
                    "animation timeline is authored against this number");
            }
        }

        [Test]
        public void NoSpellPlantsItsCasterForLongerThanTheLongestAuthoredWindUp()
        {
            var offenders = new StringBuilder();
            foreach (var spell in LoadShippedSpells())
            {
                if (spell.allowMovement) continue;   // free to walk: nothing to bound
                float planted = spell.prepareDuration + spell.channelDuration;
                if (planted > MAX_PLANT_SECONDS)
                    offenders.AppendLine($"  {spell.spellKey}: {planted:0.00}s " +
                                         $"(prepare {spell.prepareDuration:0.00}, " +
                                         $"channel {spell.channelDuration:0.00})");
            }

            Assert.That(offenders.Length, Is.Zero,
                "These spells hold their caster still for longer than the longest wind-up anyone " +
                "authored on purpose. allowMovement was inert when their values were set, so a " +
                "long phase here is inherited rather than chosen:\n" + offenders);
        }

        [Test]
        public void NoHostileSpellGainedAWindUpFromThePlayerPass()
        {
            // The eight wind-ups were authored on PLAYER spells only, deliberately: retuning a
            // monster's telegraph is a difficulty change and belongs in its own pass, next to
            // aiTuning and the FSM sets rather than beside an animation feature.
            var offenders = new StringBuilder();
            foreach (var spell in LoadShippedSpells())
            {
                if (!spell.spellKey.StartsWith("hostile_") && !spell.spellKey.StartsWith("boss_"))
                    continue;
                if (spell.prepareDuration > 0.45f)
                    offenders.AppendLine($"  {spell.spellKey}: {spell.prepareDuration:0.00}s");
            }

            Assert.That(offenders.Length, Is.Zero,
                "A hostile telegraph over 0.45 s is a difficulty change:\n" + offenders);
        }

        [Test]
        public void EveryInterruptibleSpellWithAWindUp_CanActuallyBeInterrupted()
        {
            // interruptible only means anything during Prepare — once ExecuteSpell has run there
            // is a projectile in the world and nothing to take back. A spell that is marked
            // interruptible and fires instantly is not a bug, but it IS a flag that does nothing,
            // and the count is worth knowing rather than guessing.
            int interruptibleWithWindUp = 0, interruptibleInstant = 0;
            foreach (var spell in LoadShippedSpells())
            {
                if (!spell.interruptible) continue;
                if (spell.prepareDuration > 0f) interruptibleWithWindUp++;
                else                            interruptibleInstant++;
            }

            Assert.That(interruptibleWithWindUp, Is.GreaterThan(0),
                "if nothing interruptible has a wind-up, the flag is live and still inert");
            Assert.That(interruptibleWithWindUp + interruptibleInstant, Is.GreaterThan(0));
        }
    }
}
