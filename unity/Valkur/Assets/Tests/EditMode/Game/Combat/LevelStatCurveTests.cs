using NUnit.Framework;
using UnityEngine;
using Valkur.Data;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins <see cref="LevelStatCurve"/>: linear deltas honoured, AnimationCurve
    /// override wins when populated, negative outputs are clamped to 0.
    /// </summary>
    [TestFixture]
    public class LevelStatCurveTests
    {
        private LevelStatCurve _curve;

        [SetUp]
        public void SetUp()
        {
            _curve = ScriptableObject.CreateInstance<LevelStatCurve>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_curve != null) Object.DestroyImmediate(_curve);
        }

        [Test]
        public void LinearMode_ReturnsConstantDelta()
        {
            _curve.hpPerLevel = 12;
            _curve.manaPerLevel = 4;
            Assert.AreEqual(12, _curve.HpDelta(2));
            Assert.AreEqual(12, _curve.HpDelta(50));
            Assert.AreEqual(4,  _curve.ManaDelta(2));
        }

        [Test]
        public void CurveOverride_WinsWhenPopulated()
        {
            _curve.hpPerLevel = 999; // would dominate if linear mode
            var c = new AnimationCurve();
            c.AddKey(1, 5);
            c.AddKey(10, 30);
            _curve.hpCurve = c;

            Assert.AreEqual(5,  _curve.HpDelta(1));
            // Curve interpolation between (1,5) and (10,30) at L=5 ≈ 5 + (30-5)*(4/9) ≈ 16.
            Assert.That(_curve.HpDelta(5), Is.InRange(13, 19));
            Assert.AreEqual(30, _curve.HpDelta(10));
        }

        [Test]
        public void NegativeOutput_ClampedToZero()
        {
            _curve.hpPerLevel = -5;
            Assert.AreEqual(0, _curve.HpDelta(1));

            // Curve with a negative value somewhere.
            var c = new AnimationCurve();
            c.AddKey(1, -10);
            _curve.manaCurve = c;
            Assert.AreEqual(0, _curve.ManaDelta(1));
        }
    }
}
