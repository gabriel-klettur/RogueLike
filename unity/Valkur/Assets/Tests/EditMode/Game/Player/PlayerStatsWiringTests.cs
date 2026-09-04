using System.IO;
using NUnit.Framework;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Player
{
    /// <summary>
    /// The guard that stops <see cref="StatKind"/> becoming the twelfth authored-and-inert
    /// layer in this project.
    ///
    /// Valkur has recorded the same failure over and over: <c>animation_map.json</c> that
    /// reaches no runtime code, the FSM's <c>Actions</c> and <c>Blackboard</c> blocks that
    /// round-trip to disk and are read by nothing, four casting flags with zero readers,
    /// <c>EntityStats.spawnMargin</c> and <c>feetWidthFactor</c> shown in a panel as if
    /// they mattered, <c>ItemDefinition.critChance</c> authored on 14 items and consumed
    /// only by an editor table. Each was internally consistent and disagreed only with the
    /// screen, which is exactly why each survived.
    ///
    /// So: every value of the enum must be named in the file that pushes stats into the
    /// live components, and the two derived multipliers must be named where the spell layer
    /// reads them. A new stat with no consumer is a red test on the day it is added rather
    /// than a discovery a year later.
    /// </summary>
    [TestFixture]
    public class PlayerStatsWiringTests
    {
        private static string ReadSource(string relativePath)
        {
            string path = Path.Combine(Application.dataPath, relativePath);
            Assert.IsTrue(File.Exists(path), $"Expected source file at {path}");
            return File.ReadAllText(path);
        }

        [Test]
        public void EveryStatKind_IsNamedByTheConsumerFile()
        {
            string consumers = ReadSource(
                "_Project/Scripts/Gameplay/Player/PlayerStats.Consumers.cs");

            foreach (var stat in StatCatalog.All)
            {
                StringAssert.Contains($"StatKind.{stat}", consumers,
                    $"StatKind.{stat} has no consumer in PlayerStats.Consumers.cs. Either wire " +
                    "it to a component (or a derived multiplier the spell layer reads), or " +
                    "delete the value — an authored stat that reaches no pixel is the defect " +
                    "this whole layer exists to end.");
            }
        }

        [Test]
        public void SpellLayer_ReadsTheThreeMultipliersItOwns()
        {
            string caster = ReadSource(
                "_Project/Scripts/Gameplay/Spells/Core/SpellCaster.Execution.cs");

            StringAssert.Contains("SpellManaCostMultiplier", caster,
                "Mana cost reduction has to reach the one place mana is actually consumed.");
            StringAssert.Contains("SpellCooldownMultiplier", caster,
                "Cooldown reduction has to reach the cooldown the caster actually waits.");

            string power = ReadSource("_Project/Scripts/Gameplay/Spells/Core/SpellPower.cs");
            StringAssert.Contains("SpellDamageMultiplier", power,
                "Spell power has to reach the number a spell actually deals.");
        }

        [Test]
        public void EveryStatKind_HasADisplayNameAndADescription()
        {
            // A stat with no name cannot appear on the character sheet, and a stat that
            // cannot appear on the sheet is one nobody can notice has stopped working.
            foreach (var stat in StatCatalog.All)
            {
                string name = StatCatalog.DisplayName(stat);
                Assert.IsNotEmpty(name, $"{stat} has no display name.");
                Assert.IsNotEmpty(StatCatalog.Describe(stat), $"{stat} has no description.");
            }
        }

        [Test]
        public void EveryStatKind_HasASaneClampRange()
        {
            foreach (var stat in StatCatalog.All)
            {
                float min = StatCatalog.Min(stat);
                float max = StatCatalog.Max(stat);
                Assert.Less(min, max, $"{stat} has an empty legal range.");

                // The neutral base may legitimately sit below the minimum — MaxHp rests at
                // 0 and clamps to 1, because "a character always has at least one hit point"
                // is a rule about the clamp, not about the base. What must hold is that
                // clamping the neutral value produces something inside the range and does
                // not silently invert a multiplier stat.
                float clampedNeutral = StatCatalog.Clamp(stat, StatCatalog.NeutralBase(stat));
                Assert.GreaterOrEqual(clampedNeutral, min, $"{stat} clamps below its own minimum.");
                Assert.LessOrEqual(clampedNeutral, max, $"{stat} clamps above its own maximum.");
            }
        }

        [Test]
        public void LegacyStatNames_StillParse()
        {
            // The Python build named the hit-point pool "strength" and the mana pool
            // "intelligence". Those names are still in shipped item data and in the class
            // definitions' own field names, so the parser has to keep understanding them.
            Assert.IsTrue(StatCatalog.TryParse("max_strength", out var hp));
            Assert.AreEqual(StatKind.MaxHp, hp);

            Assert.IsTrue(StatCatalog.TryParse("maxIntelligence", out var mana));
            Assert.AreEqual(StatKind.MaxMana, mana);

            Assert.IsTrue(StatCatalog.TryParse("Armor", out var def));
            Assert.AreEqual(StatKind.Defense, def);

            Assert.IsFalse(StatCatalog.TryParse("explosion_damage", out _),
                "A name that resolves to no stat must be refused, not guessed at — the " +
                "caller warns about it exactly once instead.");
        }
    }
}
