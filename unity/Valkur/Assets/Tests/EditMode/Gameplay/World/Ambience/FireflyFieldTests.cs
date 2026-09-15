using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Valkur.Core;
using Valkur.Core.Rendering;
using Valkur.Gameplay.World.Ambience;

namespace Valkur.Tests.EditMode.Gameplay.World.Ambience
{
    /// <summary>
    /// The fireflies: out only at night, never in the rain or under a roof, drawn additive on
    /// the VFX layer where the ambient light leaves them alone.
    /// </summary>
    [TestFixture]
    public class FireflyFieldTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _was;

        [SetUp] public void Snapshot() { _was = WorldLookSettings.Fireflies; WorldLookSettings.Fireflies = true; }

        [TearDown]
        public void Cleanup()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            WorldLookSettings.Fireflies = _was;
        }

        [Test]
        public void OnlyAtNight_OnlyDry_OnlyOutdoors_OnlyWhenOn()
        {
            Assert.That(FireflyField.DensityFor(0f, 0f, false, true), Is.EqualTo(1f));
            Assert.That(FireflyField.DensityFor(0.5f, 0f, false, true), Is.EqualTo(0f), "Day.");
            Assert.That(FireflyField.DensityFor(0f, 1f, false, true), Is.EqualTo(0f), "Heavy rain.");
            Assert.That(FireflyField.DensityFor(0f, 0.15f, false, true), Is.GreaterThan(0f).And.LessThan(1f), "A drizzle thins them.");
            Assert.That(FireflyField.DensityFor(0f, 0f, true, true), Is.EqualTo(0f), "Indoors.");
            Assert.That(FireflyField.DensityFor(0f, 0f, false, false), Is.EqualTo(0f), "'look fireflies off'.");
        }

        [Test]
        public void TheEmitter_IsAdditive_OnTheVFXLayer_AndFollowsTheDensity()
        {
            var go = new GameObject("fireflies");
            _spawned.Add(go);
            var field = go.AddComponent<FireflyField>();
            field.EnsureBuilt();

            var r = go.GetComponent<ParticleSystemRenderer>();
            Assert.That(r.sortingLayerName, Is.EqualTo(SortingConfig.LAYER_VFX),
                "A firefly is a light: it draws on the layer the ambient never darkens.");
            Assert.That(field.System.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World),
                "World space, or the field drags with the camera.");

            field.Tick(0f, 0f, false);
            Assert.That(field.System.emission.rateOverTime.constant, Is.EqualTo(FireflyField.RatePerScreen).Within(1e-4f));
            field.Tick(1f, 0f, false);
            Assert.That(field.System.emission.rateOverTime.constant, Is.EqualTo(0f));
        }
    }
}
