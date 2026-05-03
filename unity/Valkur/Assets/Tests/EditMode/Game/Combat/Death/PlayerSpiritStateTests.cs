using NUnit.Framework;
using UnityEngine;
using Valkur.Gameplay.Combat.Death;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    public class PlayerSpiritStateTests
    {
        private GameObject _go;
        private PlayerSpiritState _state;

        [SetUp]
        public void Setup()
        {
            _go = new GameObject("PlayerSpiritStateHost");
            _state = _go.AddComponent<PlayerSpiritState>();
        }

        [TearDown]
        public void Teardown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Default_IsNotSpirit()
        {
            Assert.IsFalse(_state.IsSpirit);
        }

        [Test]
        public void EnterSpirit_FlipsFlag_AndFiresEventOnce()
        {
            int callCount = 0;
            bool? lastValue = null;
            _state.OnSpiritStateChanged += v => { callCount++; lastValue = v; };

            _state.EnterSpirit();
            _state.EnterSpirit(); // idempotent — second call must be a no-op

            Assert.IsTrue(_state.IsSpirit);
            Assert.AreEqual(1, callCount);
            Assert.IsTrue(lastValue.HasValue && lastValue.Value);
        }

        [Test]
        public void ExitSpirit_RestoresFlag_AndFiresEventOnce()
        {
            _state.EnterSpirit();
            int callCount = 0;
            bool? lastValue = null;
            _state.OnSpiritStateChanged += v => { callCount++; lastValue = v; };

            _state.ExitSpirit();
            _state.ExitSpirit(); // idempotent

            Assert.IsFalse(_state.IsSpirit);
            Assert.AreEqual(1, callCount);
            Assert.IsTrue(lastValue.HasValue && !lastValue.Value);
        }
    }
}
