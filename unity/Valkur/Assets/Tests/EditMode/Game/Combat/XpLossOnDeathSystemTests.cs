using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="XpLossOnDeathSystem"/>: ApplyPenalty removes the
    /// configured fraction of in-current-level XP, clamped by default,
    /// honours canDelevel when explicitly enabled, no-ops on entities
    /// without an Experience component.
    /// </summary>
    [TestFixture]
    public class XpLossOnDeathSystemTests
    {
        private GameObject _systemGo;
        private XpLossOnDeathSystem _system;
        private GameObject _player;
        private Experience _xp;

        [SetUp]
        public void SetUp()
        {
            GameEvents.Clear();
            _systemGo = new GameObject("XpLossOnDeathSystem");
            _system = _systemGo.AddComponent<XpLossOnDeathSystem>();
            ForceOnEnable(_system);

            _player = new GameObject("Player");
            _xp = _player.AddComponent<Experience>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemGo != null) Object.DestroyImmediate(_systemGo);
            if (_player != null) Object.DestroyImmediate(_player);
            GameEvents.Clear();
        }

        private static void ForceOnEnable(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(mb, null);
        }

        [Test]
        public void ApplyPenalty_RemovesConfiguredFractionOfInLevelXp()
        {
            _xp.AddXp(200); // default formula → L1 (100), in-level XP = 100.
            _system.LossFraction = 0.5f; // 50% of 100 = 50.

            int actual = _system.ApplyPenalty(_player);

            Assert.AreEqual(50, actual);
            Assert.AreEqual(150, _xp.TotalXp);
            Assert.AreEqual(1, _xp.Level, "Default clampToCurrentLevel keeps the player at L1.");
        }

        [Test]
        public void ApplyPenalty_DoesNotDelevel_WhenCanDelevelFalse()
        {
            _xp.AddXp(110); // L1 + 10 in-level.
            _system.LossFraction = 1f; // would remove all 10 in-level XP.
            _system.CanDelevel = false;

            int actual = _system.ApplyPenalty(_player);

            Assert.AreEqual(10, actual);
            Assert.AreEqual(1, _xp.Level);
            Assert.AreEqual(100, _xp.TotalXp);
        }

        [Test]
        public void ApplyPenalty_NullEntity_IsNoOp()
        {
            int actual = _system.ApplyPenalty(null);
            Assert.AreEqual(0, actual);
            Assert.AreEqual(0, _system.LastApplied);
        }

        [Test]
        public void ApplyPenalty_EntityWithoutExperience_IsNoOp()
        {
            var bare = new GameObject("Bare");
            try
            {
                int actual = _system.ApplyPenalty(bare);
                Assert.AreEqual(0, actual);
            }
            finally { Object.DestroyImmediate(bare); }
        }

        [Test]
        public void ZeroLossFraction_DisablesPenalty()
        {
            _xp.AddXp(200);
            _system.LossFraction = 0f;
            int before = _xp.TotalXp;

            _system.ApplyPenalty(_player);

            Assert.AreEqual(before, _xp.TotalXp);
        }

        [Test]
        public void ApplyPenalty_RoundsToInt_NoFractionalLoss()
        {
            _xp.AddXp(101); // L1 + 1 in-level.
            _system.LossFraction = 0.4f; // 1 * 0.4 = 0.4 → rounds to 0.

            int actual = _system.ApplyPenalty(_player);

            Assert.AreEqual(0, actual,
                "Loss values that round to 0 must short-circuit before calling RemoveXp.");
        }
    }
}
