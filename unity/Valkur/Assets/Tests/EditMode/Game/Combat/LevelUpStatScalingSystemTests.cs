using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="LevelUpStatScalingSystem"/>: with a curve, level-ups
    /// permanently grow MaxHp/MaxMana on the levelled entity. Without a
    /// curve the system is a silent no-op. Missing components on the
    /// entity are skipped without errors.
    /// </summary>
    [TestFixture]
    public class LevelUpStatScalingSystemTests
    {
        private GameObject _systemGo;
        private LevelUpStatScalingSystem _system;
        private GameObject _entity;
        private Health _hp;
        private Mana _mp;
        private LevelStatCurve _curve;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();

            _systemGo = new GameObject("LevelUpStatScalingSystem");
            _system = _systemGo.AddComponent<LevelUpStatScalingSystem>();
            ForceOnEnable(_system);

            _entity = new GameObject("Entity");
            _hp = _entity.AddComponent<Health>();
            _hp.Initialize(100);
            _mp = _entity.AddComponent<Mana>();
            _mp.Initialize(50);

            _curve = ScriptableObject.CreateInstance<LevelStatCurve>();
            _curve.hpPerLevel = 8;
            _curve.manaPerLevel = 3;
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            if (_entity != null) Object.DestroyImmediate(_entity);
            if (_curve != null) Object.DestroyImmediate(_curve);
            GameEvents.Clear();
        }

        private static void ForceOnEnable(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(mb, null);
        }

        [Test]
        public void NoCurve_LevelUp_IsNoOp()
        {
            int hpBefore = _hp.MaxHp;
            int mpBefore = _mp.MaxMana;

            GameEvents.FireLevelUp(_entity, 2);

            Assert.AreEqual(hpBefore, _hp.MaxHp);
            Assert.AreEqual(mpBefore, _mp.MaxMana);
        }

        [Test]
        public void WithCurve_LevelUp_GrowsMaxHpAndMaxMana()
        {
            _system.SetCurve(_curve);
            int hpBefore = _hp.MaxHp;
            int mpBefore = _mp.MaxMana;

            GameEvents.FireLevelUp(_entity, 2);

            Assert.AreEqual(hpBefore + 8, _hp.MaxHp);
            Assert.AreEqual(mpBefore + 3, _mp.MaxMana);
        }

        [Test]
        public void EntityWithoutMana_HpStillGrows()
        {
            _system.SetCurve(_curve);
            var hpOnly = new GameObject("HpOnly");
            try
            {
                var h = hpOnly.AddComponent<Health>();
                h.Initialize(50);
                int before = h.MaxHp;

                GameEvents.FireLevelUp(hpOnly, 2);

                Assert.AreEqual(before + 8, h.MaxHp);
            }
            finally { Object.DestroyImmediate(hpOnly); }
        }

        [Test]
        public void NullEntity_IsHandledGracefully()
        {
            _system.SetCurve(_curve);
            Assert.DoesNotThrow(() => GameEvents.FireLevelUp(null, 2));
        }

        [Test]
        public void MultipleLevelUps_AccumulateGrowth()
        {
            _system.SetCurve(_curve);
            int hpBefore = _hp.MaxHp;

            GameEvents.FireLevelUp(_entity, 2);
            GameEvents.FireLevelUp(_entity, 3);
            GameEvents.FireLevelUp(_entity, 4);

            Assert.AreEqual(hpBefore + 8 * 3, _hp.MaxHp,
                "Three sequential level-ups must apply the per-level delta three times.");
        }
    }
}
