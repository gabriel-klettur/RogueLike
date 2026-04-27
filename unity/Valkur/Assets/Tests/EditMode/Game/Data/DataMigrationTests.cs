using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Data
{
    /// <summary>
    /// Golden data tests: validate that Python JSON data was correctly
    /// migrated into Unity ScriptableObjects.
    /// </summary>
    public class DataMigrationTests
    {
        private const string CATALOGS = "Assets/_Project/Data/Catalogs";

        [Test]
        public void Monster_Barbol_HasCorrectStats()
        {
            var barbol = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(
                $"{CATALOGS}/Monsters/barbol.asset");
            Assert.IsNotNull(barbol, "barbol.asset should exist");
            Assert.AreEqual("barbol", barbol.monsterKey);
            Assert.AreEqual(100, barbol.stats.hp);
            Assert.AreEqual(5, barbol.stats.meleeDamage);
            Assert.AreEqual(5, barbol.stats.defense);
            Assert.AreEqual(10, barbol.stats.power);
            Assert.AreEqual("EVIL", barbol.stats.faction);
            Assert.AreEqual("Monster_Default", barbol.fsmSet);
            Assert.IsTrue(barbol.useAttackTelegraph);
        }

        [Test]
        public void Player_Dwarf_HasCorrectStats()
        {
            var dwarf = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                $"{CATALOGS}/Players/dwarf.asset");
            Assert.IsNotNull(dwarf, "dwarf.asset should exist");
            Assert.AreEqual("dwarf", dwarf.playerKey);
            Assert.AreEqual(200, dwarf.maxStrength);
            Assert.AreEqual(35, dwarf.maxIntelligence);
            Assert.AreEqual(90, dwarf.maxDexterity);
            Assert.AreEqual(45, dwarf.initialStrength);
            Assert.AreEqual(4, dwarf.basicSpeed);
            Assert.AreEqual(5, dwarf.basicArmor);
            Assert.AreEqual(4, dwarf.dashCharges);
        }

        [Test]
        public void Spell_Fireball_HasCorrectConfig()
        {
            var fireball = AssetDatabase.LoadAssetAtPath<SpellDefinition>(
                $"{CATALOGS}/Spells/fireball.asset");
            Assert.IsNotNull(fireball, "fireball.asset should exist");
            Assert.AreEqual("fireball", fireball.spellKey);
            Assert.AreEqual(SpellType.Projectile, fireball.type);
            Assert.AreEqual(20f, fireball.damage);
            Assert.AreEqual(1f, fireball.manaCost);
            // Hand-tuned values (not raw Python parity): see chat history.
            // Range/speed were lowered for better game feel during gameplay testing.
            Assert.AreEqual(15f, fireball.range);
            Assert.AreEqual(16f, fireball.speed);
        }

        [Test]
        public void AllPlayers_ExistAndHaveKeys()
        {
            string[] expected = { "dwarf", "barbarian", "elven", "mague", "valkyrie" };
            foreach (var key in expected)
            {
                var player = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(
                    $"{CATALOGS}/Players/{key}.asset");
                Assert.IsNotNull(player, $"Player '{key}' should exist");
                Assert.AreEqual(key, player.playerKey);
            }
        }

        [Test]
        public void Monsters_CountMatchesExpected()
        {
            var guids = AssetDatabase.FindAssets("t:MonsterDefinition", new[] { $"{CATALOGS}/Monsters" });
            Assert.GreaterOrEqual(guids.Length, 10,
                "Should have at least 10 monster definitions imported");
        }

        [Test]
        public void Spells_CountMatchesExpected()
        {
            var guids = AssetDatabase.FindAssets("t:SpellDefinition", new[] { $"{CATALOGS}/Spells" });
            Assert.GreaterOrEqual(guids.Length, 15,
                "Should have at least 15 spell definitions imported");
        }
    }
}
