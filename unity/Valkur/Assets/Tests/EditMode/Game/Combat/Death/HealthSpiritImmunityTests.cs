using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Health.TakeDamage must short-circuit while the player is in spirit form,
    /// regardless of HP value or invincibility flag. Otherwise NPCs that linger
    /// in attack range during the spirit walk would chip the resurrection HP
    /// once the player revives.
    /// </summary>
    public class HealthSpiritImmunityTests
    {
        private GameObject _player;
        private Health _health;
        private PlayerSpiritState _spirit;

        [SetUp]
        public void Setup()
        {
            _player = new GameObject("Player");
            _player.tag = "Player";
            _health = _player.AddComponent<Health>();
            _health.Initialize(100);
            _spirit = _player.AddComponent<PlayerSpiritState>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_player != null) Object.DestroyImmediate(_player);
        }

        [Test]
        public void NormalDamage_AppliesWhenAlive()
        {
            _health.TakeDamage(30);
            Assert.AreEqual(70, _health.CurrentHp);
            Assert.IsFalse(_health.IsDead);
        }

        [Test]
        public void DamageWhileSpirit_IsIgnored()
        {
            _spirit.EnterSpirit();
            _health.TakeDamage(50);
            Assert.AreEqual(100, _health.CurrentHp);
        }

        [Test]
        public void DamageResumesAfterExitingSpirit()
        {
            _spirit.EnterSpirit();
            _health.TakeDamage(50);
            _spirit.ExitSpirit();
            _health.TakeDamage(20);
            Assert.AreEqual(80, _health.CurrentHp);
        }
    }
}
