using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// THE RESIZE MUST NOT INTERRUPT THE EFFECT.
    ///
    /// This fixture exists because it did. <c>SetOverrides</c> originally re-applied the whole
    /// preset, and ApplyPreset opens with <c>Stop(StopEmittingAndClear)</c> — correct when an
    /// emitter is being handed a different effect, catastrophic when it is called from a drag
    /// handle sixty times a second. Every particle alive was destroyed on every frame of the
    /// gesture: the leaves stopped falling while the author resized the box they fall out of,
    /// and took a full lifespan to come back after the mouse was released. Measured, the live
    /// count sat at 0 for the entire drag.
    ///
    /// The fix is a live path that rewrites only the modules an override can move — shape,
    /// throw, gravity, velocity, noise — on systems that keep playing. What follows pins both
    /// halves of that: nothing is interrupted, and the new size actually reaches the particles.
    ///
    /// Note on measuring "still falling": <c>ParticleSystem.Particle.velocity</c> does NOT
    /// include the velocity-over-lifetime module, which is where a preset's drift lives, so a
    /// falling leaf reads as velocity ~0 there. The module itself is the honest thing to
    /// assert, and the sweep at the end checks the population instead.
    /// </summary>
    [TestFixture]
    public class ParticleLiveResizeTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>Frames of a resize gesture to simulate. Any wipe shows on the first one.</summary>
        private const int DRAG_FRAMES = 30;

        private readonly List<GameObject> _created = new List<GameObject>();
        private ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[4096];

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Harness ──────────────────────────────────────────────────────────────

        private static ParticlePresetCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ParticlePresetCatalog>(CATALOG_PATH);
            Assert.IsTrue(catalog != null, $"ParticlePresetCatalog not found at {CATALOG_PATH}.");
            return catalog;
        }

        private static ParticlePresetDefinition Shipped(string id)
        {
            var preset = LoadCatalog().GetById(id);
            Assert.IsTrue(preset != null, $"'{id}' is missing from the catalog.");
            return preset;
        }

        /// <summary>An emitter warmed up to its steady state, the way a placed one is.</summary>
        private ParticleEmitter Warm(ParticlePresetDefinition preset, float scale = 1f, float seconds = 4f)
        {
            var go = new GameObject("ResizeProbe_" + preset.id);
            _created.Add(go);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scale);
            ParticleTestDeterminism.PinRandomness(go);
            Step(emitter, seconds);
            return emitter;
        }

        /// <summary>
        /// Advances every system by <paramref name="seconds"/>. Play() first because Simulate
        /// PAUSES the system it advances — without it the second step of any test would be
        /// measuring a frozen emitter and would pass for the wrong reason.
        /// </summary>
        private static void Step(ParticleEmitter emitter, float seconds)
        {
            foreach (var ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Play();
                ps.Simulate(seconds, true, false, true);
            }
        }

        private static ParticleSystem RootSystem(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>(true);

        private static int LiveCount(ParticleEmitter emitter)
        {
            int total = 0;
            foreach (var ps in emitter.GetComponentsInChildren<ParticleSystem>(true))
                total += ps.particleCount;
            return total;
        }

        /// <summary>Runs a resize gesture frame by frame and reports the emptiest moment.</summary>
        private int LowestCountDuringDrag(ParticleEmitter emitter, ParticleInstanceOverrides target)
        {
            int lowest = int.MaxValue;

            for (int i = 0; i < DRAG_FRAMES; i++)
            {
                float t = (i + 1) / (float)DRAG_FRAMES;
                emitter.SetOverrides(new ParticleInstanceOverrides(
                    Mathf.Lerp(1f, target.spawnScaleX, t),
                    Mathf.Lerp(1f, target.spawnScaleY, t),
                    Mathf.Lerp(1f, target.reachScale, t)));

                Step(emitter, 1f / 60f);
                lowest = Mathf.Min(lowest, LiveCount(emitter));
            }

            return lowest;
        }

        // ── The regression ───────────────────────────────────────────────────────

        [Test]
        public void ResizingTheEmissionBox_NeverEmptiesTheEffect()
        {
            var emitter = Warm(Shipped("falling_leaf_30s"));
            int before = LiveCount(emitter);
            Assert.Greater(before, 0, "The probe never reached its steady state.");

            int lowest = LowestCountDuringDrag(emitter, new ParticleInstanceOverrides(2.5f, 2.5f, 1f));

            Assert.Greater(lowest, 0,
                "Every frame of the drag destroyed every live leaf: the field stopped raining " +
                "for as long as the author was resizing it.");
        }

        [Test]
        public void ResizingTheReach_NeverEmptiesTheEffect()
        {
            var emitter = Warm(Shipped("falling_leaf_30s"));
            int lowest = LowestCountDuringDrag(emitter, new ParticleInstanceOverrides(1f, 1f, 3f));

            Assert.Greater(lowest, 0);
        }

        [Test]
        public void ShrinkingBothAtOnce_NeverEmptiesTheEffect()
        {
            var emitter = Warm(Shipped("falling_leaf_30s"));
            int lowest = LowestCountDuringDrag(emitter, new ParticleInstanceOverrides(0.3f, 0.3f, 0.3f));

            Assert.Greater(lowest, 0);
        }

        [Test]
        public void ResizingAComposite_NeverEmptiesAnyOfItsLayers()
        {
            var emitter = Warm(Shipped("flowers_pollen_soft"), 1f, 6f);
            int lowest = LowestCountDuringDrag(emitter, new ParticleInstanceOverrides(2f, 1.5f, 2f));

            Assert.Greater(lowest, 0,
                "One placed composite is a root plus three layer systems; a rebuild wipes all " +
                "four together.");
        }

        [Test]
        public void AResize_DoesNotStopASystemThatIsPlaying()
        {
            // No Simulate anywhere in this one: Simulate PAUSES the system it advances, so a
            // warmed-up probe reads as not-playing whatever the resize did, and the assertion
            // would pass for the wrong reason.
            var go = new GameObject("ResizePlayingProbe");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(Shipped("falling_leaf_30s"), 1f);
            ParticleTestDeterminism.PinRandomness(go);

            var ps = RootSystem(emitter);
            Assert.IsTrue(ps.isPlaying, "ApplyPreset leaves the system playing.");

            emitter.SetOverrides(new ParticleInstanceOverrides(1.8f, 1.2f, 1.4f));

            Assert.IsTrue(ps.isPlaying, "A resize must not stop the system.");
            Assert.Greater(ps.emission.rateOverTime.constant, 0f, "Nor silence its emission.");
        }

        [Test]
        public void AfterAResize_TheEmitterKeepsProducingParticles()
        {
            var emitter = Warm(Shipped("falling_leaf_30s"));

            emitter.SetOverrides(new ParticleInstanceOverrides(1.8f, 1.2f, 1.4f));

            // A full lifespan later every particle that predates the resize is gone, so a
            // population here is one the emitter produced AFTER it.
            Step(emitter, 3f);
            Assert.Greater(LiveCount(emitter), 0);
        }

        // ── The new size has to actually reach the particles ─────────────────────

        [Test]
        public void ResizingTheEmissionBox_MovesTheShapeItSpawnsFrom()
        {
            var emitter = Warm(Shipped("falling_leaf_30s"));
            Vector3 before = RootSystem(emitter).shape.scale;

            emitter.SetOverrides(new ParticleInstanceOverrides(2f, 2f, 1f));
            Vector3 after = RootSystem(emitter).shape.scale;

            Assert.AreEqual(before.x * 2f, after.x, 1e-3f);
            Assert.AreEqual(before.z * 2f, after.z, 1e-3f,
                "The spawn box's height lands on the shape's local Z — see BoxRotationFor.");
        }

        [Test]
        public void ResizingTheReach_ScalesTheDriftThatMakesLeavesFall()
        {
            var preset = Shipped("falling_leaf_30s");
            Assert.IsTrue(preset.vfx.useGravityVector, "This test assumes a drift-driven fall.");
            float authored = preset.vfx.gravityVector.y;

            var emitter = Warm(preset);
            emitter.SetOverrides(new ParticleInstanceOverrides(1f, 1f, 3f));

            var vel = RootSystem(emitter).velocityOverLifetime;
            Assert.IsTrue(vel.enabled, "Turning the fall off is exactly the bug being guarded.");
            Assert.AreEqual(authored * 3f, vel.y.constant, 1e-3f,
                "Reach multiplies every motion term, and for a leaf field the drift IS the fall.");
            Assert.Less(vel.y.constant, 0f, "Still downward.");
        }

        [Test]
        public void ResizingAComposite_ReachesItsLayersToo()
        {
            var preset = Shipped("flowers_pollen_soft");
            var emitter = Warm(preset, 1f, 6f);

            var systems = emitter.GetComponentsInChildren<ParticleSystem>(true);
            Assert.Greater(systems.Length, 1, "This preset is expected to carry layers.");

            var before = new List<Vector3>();
            foreach (var ps in systems) before.Add(ps.shape.scale);

            emitter.SetOverrides(new ParticleInstanceOverrides(2f, 2f, 1f));

            for (int i = 0; i < systems.Length; i++)
            {
                Assert.AreEqual(before[i].x * 2f, systems[i].shape.scale.x, 1e-3f,
                    $"Layer {i} kept the preset's width, so the marker around the stack would " +
                    "no longer describe it.");
            }
        }

        [Test]
        public void AResizeSurvivesThePresetBeingReAppliedUnderIt()
        {
            var preset = Shipped("falling_leaf_30s");
            var emitter = Warm(preset);

            emitter.SetOverrides(new ParticleInstanceOverrides(2f, 1f, 1f));
            Vector3 resized = RootSystem(emitter).shape.scale;

            // The F1 editor re-applies the preset to every live emitter on each property edit,
            // and the culling loader re-applies on re-enable.
            emitter.ApplyPreset(preset, 1f);
            ParticleTestDeterminism.PinRandomness(emitter.gameObject);

            Assert.AreEqual(resized.x, RootSystem(emitter).shape.scale.x, 1e-3f);
        }

        // ── Whole catalog ────────────────────────────────────────────────────────

        [Test]
        public void NoLoopingPresetInTheCatalog_StopsEmittingWhenItIsResized()
        {
            var failures = new StringBuilder();
            int checkedPresets = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (!preset.vfx.loops) continue;                 // a burst is over by design
                if (preset.vfx.kind == "lightning") continue;    // LineRenderer, no systems

                var go = new GameObject("ResizeSweep");
                var emitter = go.AddComponent<ParticleEmitter>();
                emitter.ApplyPreset(preset, 1f);
                ParticleTestDeterminism.PinRandomness(go);
                Step(emitter, 4f);

                if (LiveCount(emitter) == 0) { Object.DestroyImmediate(go); continue; }
                checkedPresets++;

                emitter.SetOverrides(new ParticleInstanceOverrides(1.7f, 1.7f, 1.7f));

                int immediately = LiveCount(emitter);
                Step(emitter, 1f);
                int later = LiveCount(emitter);

                if (immediately == 0)
                    failures.Append($"'{preset.id}': the resize destroyed every live particle.\n");
                else if (later == 0)
                    failures.Append($"'{preset.id}': alive through the resize, then dead a second later — " +
                                    "it stopped emitting.\n");

                Object.DestroyImmediate(go);
            }

            Assert.Greater(checkedPresets, 50,
                "Almost nothing was exercised — the sweep is passing over an empty set.");
            Assert.IsEmpty(failures.ToString(),
                $"Resizing an emitter must never interrupt it, for any preset.\n{failures}");
        }
    }
}
