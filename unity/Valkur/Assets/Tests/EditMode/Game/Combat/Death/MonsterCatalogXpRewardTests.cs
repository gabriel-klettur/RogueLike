using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Catalog-level audit: every <see cref="MonsterDefinition"/> in the
    /// shipped <c>MonsterCatalog.asset</c> has been tuned with an explicit
    /// xpReward value (or 0 if it's a friendly NPC), so the runtime path
    /// never silently relies on the legacy heuristic for a known monster.
    ///
    /// Tier audit:
    ///   • Hostiles must have xpReward &gt; 0 (any positive value passes —
    ///     the test doesn't pin specific numbers, only the contract).
    ///   • Vendors / friendly NPCs (faction != EVIL or monsterKey starts
    ///     with "vendor_") must have xpReward = 0 — they should not be
    ///     farmable for XP.
    /// </summary>
    [TestFixture]
    public class MonsterCatalogXpRewardTests
    {
        private const string CatalogPath =
            "Assets/_Project/Data/Catalogs/Monsters/MonsterCatalog.asset";

        private MonsterCatalog _catalog;

        [SetUp]
        public void LoadCatalog()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            Assert.IsNotNull(_catalog,
                $"MonsterCatalog asset must exist at {CatalogPath}. " +
                "If the project layout moved, update the test constant.");
        }

        [Test]
        public void Catalog_NotEmpty()
        {
            Assert.That(_catalog.Definitions.Count, Is.GreaterThan(0),
                "Catalog must contain at least one monster definition.");
        }

        [Test]
        public void EveryDefinition_IsAssigned()
        {
            for (int i = 0; i < _catalog.Definitions.Count; i++)
            {
                Assert.IsNotNull(_catalog.Definitions[i],
                    $"Catalog slot {i} is null — re-import the catalog from the migrator.");
            }
        }

        [Test]
        public void HostileMonsters_HavePositiveXpReward()
        {
            foreach (var def in _catalog.Definitions)
            {
                if (def == null) continue;
                if (IsFriendly(def)) continue;

                Assert.That(def.xpReward, Is.GreaterThan(0),
                    $"Hostile '{def.monsterKey}' has xpReward = {def.xpReward}. " +
                    "Every hostile must grant positive XP. Set xpReward in the asset YAML " +
                    "or rely on the heuristic by leaving the field at 0 only when the " +
                    "intent is documented.");
            }
        }

        [Test]
        public void Vendors_HaveZeroXpReward()
        {
            foreach (var def in _catalog.Definitions)
            {
                if (def == null) continue;
                if (!IsFriendly(def)) continue;

                Assert.AreEqual(0, def.xpReward,
                    $"Vendor / friendly NPC '{def.monsterKey}' has xpReward = " +
                    $"{def.xpReward}. Friendly NPCs must not be farmable for XP — " +
                    "set xpReward to 0.");
            }
        }

        [Test]
        public void ComputeXpReward_MatchesAssetValueForEveryHostile()
        {
            // Smoke-tests that the runtime resolution path agrees with what the
            // asset stores — catches accidental shadowing if a refactor
            // introduces a different precedence ordering in ComputeXpReward.
            foreach (var def in _catalog.Definitions)
            {
                if (def == null) continue;
                if (IsFriendly(def)) continue;

                int resolved = DeathDropSystem.ComputeXpReward(def, maxHpFallback: 0);
                Assert.AreEqual(def.xpReward, resolved,
                    $"ComputeXpReward({def.monsterKey}) returned {resolved}, " +
                    $"but the asset stores xpReward = {def.xpReward}. " +
                    "These must agree for any positive xpReward.");
            }
        }

        private static bool IsFriendly(MonsterDefinition def)
        {
            if (string.IsNullOrEmpty(def.monsterKey)) return false;
            return def.monsterKey.StartsWith("vendor_");
        }
    }
}
