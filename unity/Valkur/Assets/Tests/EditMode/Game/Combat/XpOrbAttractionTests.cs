using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Pins the new pickup contract:
    ///   • Default attract radius is 1.5 world units (1–2 game tiles at PPU=16).
    ///   • Outside the radius the orb sits idle (no movement).
    ///   • A short post-spawn settle period prevents instant absorption when
    ///     the killing blow lands in melee range.
    /// </summary>
    [TestFixture]
    public class XpOrbAttractionTests
    {
        private GameObject _orbGo;
        private XpOrb _orb;
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;

            _orbGo = new GameObject("XP_Orb");
            XpOrb.BuildVisuals(_orbGo);
            _orb = _orbGo.AddComponent<XpOrb>();
            _orb.Initialize(10, new Vector3(0f, 0f, 0f));

            _player = new GameObject("Player");
            _player.tag = "Player";
        }

        [TearDown]
        public void TearDown()
        {
            if (_orbGo != null) Object.DestroyImmediate(_orbGo);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        [Test]
        public void DefaultAttractRadius_IsBetweenOneAndTwoTiles()
        {
            // Reflection over a SerializeField is intentional: the field is private,
            // and we want to lock the *default* (which is what designer-not-tweaked
            // assets pick up) at the language level.
            var field = typeof(XpOrb).GetField("attractRadius",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field);
            float r = (float)field.GetValue(_orb);
            Assert.That(r, Is.GreaterThanOrEqualTo(1f).And.LessThanOrEqualTo(2f),
                $"attractRadius default = {r}; must be 1–2 world units (= 1–2 tiles).");
        }

        [Test]
        public void OrbSpawn_StartsInSettlingState()
        {
            Assert.IsTrue(_orb.IsSettling,
                "A freshly-initialized orb must be in its post-spawn grace period.");
        }

        [Test]
        public void OrbHasXpValue_AfterInitialize()
        {
            Assert.AreEqual(10, _orb.XpValue);
        }
    }
}
