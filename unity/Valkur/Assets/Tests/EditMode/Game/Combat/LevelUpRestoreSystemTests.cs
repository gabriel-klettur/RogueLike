using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="LevelUpRestoreSystem"/>: OnLevelUp restores HP+MP
    /// to full (or to the configured fraction), missing components are
    /// silent no-ops, and unrelated entities are not affected by another
    /// entity's level-up.
    /// </summary>
    [TestFixture]
    public class LevelUpRestoreSystemTests
    {
        private GameObject _systemGo;
        private LevelUpRestoreSystem _system;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _systemGo = new GameObject("LevelUpRestoreSystem");
            _system = _systemGo.AddComponent<LevelUpRestoreSystem>();

            // EditMode AddComponent doesn't reliably fire OnEnable; force
            // the GameEvents subscription via reflection.
            var onEnable = typeof(LevelUpRestoreSystem).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            onEnable.Invoke(_system, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            GameEvents.Clear();
        }

        // ── Behaviours ──────────────────────────────────────────────────────────

        [Test]
        public void LevelUp_RestoresHealthAndMana()
        {
            var entity = new GameObject("Player");
            try
            {
                var health = entity.AddComponent<Health>();
                health.Initialize(100);
                health.TakeDamage(60); // current = 40

                var mana = entity.AddComponent<Mana>();
                mana.Initialize(50, regen: 0f);
                mana.TryConsume(30); // current = 20

                GameEvents.FireLevelUp(entity, 2);

                Assert.AreEqual(100, health.CurrentHp,
                    "Level-up must fully restore HP at the default 1.0 fraction.");
                Assert.AreEqual(50, mana.CurrentMana,
                    "Level-up must fully restore Mana at the default 1.0 fraction.");
            }
            finally { Object.DestroyImmediate(entity); }
        }

        [Test]
        public void LevelUp_NoHealthComponent_DoesNotThrow()
        {
            // An entity with Mana but no Health (rare, but possible for
            // pure-spellcaster NPCs) must not crash the level-up handler.
            var entity = new GameObject("ManaOnly");
            try
            {
                var mana = entity.AddComponent<Mana>();
                mana.Initialize(50, regen: 0f);
                mana.TryConsume(50);

                Assert.DoesNotThrow(() => GameEvents.FireLevelUp(entity, 2));
                Assert.AreEqual(50, mana.CurrentMana,
                    "Mana must still restore even when Health is absent.");
            }
            finally { Object.DestroyImmediate(entity); }
        }

        [Test]
        public void LevelUp_OnlyAffectsTheLeveledEntity()
        {
            var levelled = new GameObject("Levelled");
            var bystander = new GameObject("Bystander");
            try
            {
                var levelledHp = levelled.AddComponent<Health>();
                levelledHp.Initialize(100);
                levelledHp.TakeDamage(50); // 50/100

                var bystanderHp = bystander.AddComponent<Health>();
                bystanderHp.Initialize(100);
                bystanderHp.TakeDamage(50); // 50/100

                GameEvents.FireLevelUp(levelled, 2);

                Assert.AreEqual(100, levelledHp.CurrentHp,
                    "Levelled entity must be restored.");
                Assert.AreEqual(50, bystanderHp.CurrentHp,
                    "Bystander HP must remain untouched — the system must not " +
                    "reach for FindObjectsOfType<Health>() and heal everyone.");
            }
            finally
            {
                Object.DestroyImmediate(levelled);
                Object.DestroyImmediate(bystander);
            }
        }

        [Test]
        public void RestoreFraction_HalfHeal_PartialRestore()
        {
            // Set the inspector field to 0.5 via reflection so the test
            // doesn't need a serialized prefab.
            var f = typeof(LevelUpRestoreSystem).GetField("restoreFraction",
                BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(_system, 0.5f);

            var entity = new GameObject("Player");
            try
            {
                var health = entity.AddComponent<Health>();
                health.Initialize(100);
                health.TakeDamage(80); // 20/100

                GameEvents.FireLevelUp(entity, 2);

                // 20 + 50 (= 100*0.5) = 70.
                Assert.AreEqual(70, health.CurrentHp,
                    $"Half-heal restore must bump HP by 50 from 20 to 70 — " +
                    $"got {health.CurrentHp}.");
            }
            finally { Object.DestroyImmediate(entity); }
        }
    }
}
