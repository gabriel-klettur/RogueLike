using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Ambience;

namespace Valkur.Tests.EditMode.Game.World.Ambience
{
    /// <summary>
    /// The player's lantern: an additive point light that exists only after dusk, ramps
    /// rather than switches, and can be put out from the console.
    /// </summary>
    [TestFixture]
    public class PlayerLanternTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _was;

        [SetUp] public void Snapshot() { _was = WorldLookSettings.Lantern; WorldLookSettings.Lantern = true; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            WorldLookSettings.Lantern = _was;
        }

        private PlayerLantern Player()
        {
            var go = new GameObject("player");
            _spawned.Add(go);
            return PlayerLantern.Attach(go);
        }

        [Test]
        public void TheLight_IsAnAdditivePoint_WithATorchsOwnBlendStyle()
        {
            var l = Player().Light;
            Assert.IsNotNull(l);
            Assert.That(l.lightType, Is.EqualTo(Light2D.LightType.Point));
            Assert.That(l.blendStyleIndex, Is.EqualTo(1), "Additive, like every placed torch: a Multiply lantern could only darken.");
            Assert.That(l.pointLightOuterRadius, Is.EqualTo(PlayerLantern.OuterRadius));
            Assert.That(l.falloffIntensity, Is.InRange(0f, 1f), "URP clamps falloff to [0,1].");
        }

        [Test]
        public void Noon_NoLantern_Midnight_Full_Dusk_Between()
        {
            Assert.That(PlayerLantern.IntensityFor(1f, false, true), Is.EqualTo(0f), "Noon, lights off.");
            Assert.That(PlayerLantern.IntensityFor(0f, true, true), Is.EqualTo(PlayerLantern.NightIntensity).Within(1e-4f));
            float dusk = PlayerLantern.IntensityFor(0.5f, true, true);
            Assert.That(dusk, Is.GreaterThan(0f).And.LessThan(PlayerLantern.NightIntensity));
            Assert.That(PlayerLantern.IntensityFor(0f, true, false), Is.EqualTo(0f), "'look lantern off'.");
        }

        [Test]
        public void TheLantern_RampsIn_RatherThanSwitching()
        {
            var lantern = Player();
            lantern.Tick(0.016f, 0f, true);
            Assert.IsTrue(lantern.Light.enabled);
            Assert.That(lantern.Light.intensity, Is.GreaterThan(0f).And.LessThan(PlayerLantern.NightIntensity * 0.5f),
                "One frame after dusk the lantern is faint: a light that pops on is a switch.");
            for (int i = 0; i < 120; i++) lantern.Tick(0.016f, 0f, true);
            Assert.That(lantern.Light.intensity, Is.EqualTo(PlayerLantern.NightIntensity).Within(1e-3f));

            for (int i = 0; i < 120; i++) lantern.Tick(0.016f, 1f, false);
            Assert.IsFalse(lantern.Light.enabled, "And it goes out at dawn — the object, not only the value.");
        }

        [Test]
        public void ItIsDimmerThanATorch()
        {
            // The town's fixtures must stay the brighter things; a lantern that outshone a
            // torch would make the placed lights decoration. Measured at 0.62: the pool blew the
            // character out to a white blob. The torch preset ships at 0.55.
            Assert.That(PlayerLantern.NightIntensity, Is.LessThan(0.5f));
            Assert.That(PlayerLantern.NightIntensity, Is.GreaterThan(0.15f), "And bright enough to see the ground by.");
        }
    }
}
