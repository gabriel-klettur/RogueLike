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
    /// THE COVERAGE GUARANTEE. Runs every preset in the shipped catalog, at two placement
    /// scales and several points in its life, and asserts that the marker the F1 editor draws
    /// actually contains every particle the preset has on screen.
    ///
    /// Nothing else can establish this. <see cref="ParticleFootprint"/> models a dozen Unity
    /// modules — emission shapes, constant drift, scalar gravity, the random initial throw,
    /// radial pull, orbital velocity, noise, size curves, per-particle rotation — and every
    /// one of those models is an approximation of code nobody here wrote. Reasoning found the
    /// terms; only simulating the real systems and looking at where the particles ended up
    /// found the four defects that survived the reasoning:
    ///
    ///  • The prediction reserved the AVERAGE initial speed. startSpeed is a random 0..speed
    ///    per particle, so 34 presets had particles outside their own marker.
    ///  • Size curves were read at their KEYS. <c>ParticleEmitter</c> builds them with
    ///    <c>new AnimationCurve(keys)</c>, which smooths the tangents and therefore overshoots
    ///    BETWEEN keys — the pollen quads grew 9% larger than any key said.
    ///  • Noise was reserved as a fixed offset. The field scrolls, so the displacement is a
    ///    walk that keeps accumulating over a particle's life; the 7-second haze layers ended
    ///    up three times their authored strength away.
    ///  • The 8-unit cap was applied to MEASUREMENTS too, so a fountain that really does throw
    ///    water 20 units down was marked with a box it left immediately.
    ///
    /// The measured path (<see cref="ParticleFootprint.OfLive"/>) is held to zero tolerance:
    /// it is what the editor actually draws. The predicted path is held to the same bar except
    /// where it says <see cref="ParticleFootprint.Clipped"/>, which is its way of admitting it
    /// is a handle rather than a bound.
    /// </summary>
    [TestFixture]
    public class ParticleFootprintCoverageTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>Placement scales to exercise. The instance record allows any multiplier;
        /// 1 and 2 catch anything that scales a term it should not, or fails to.</summary>
        private static readonly float[] Scales = { 1f, 2f };

        /// <summary>
        /// Seconds of simulation before each measurement. Short samples catch a preset whose
        /// particles are still spreading; long ones catch the accumulating terms (noise, drift)
        /// that only show up after several generations have come and gone.
        /// </summary>
        private static readonly float[] SimulationTimes = { 2.5f, 7f };

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

        /// <summary>
        /// World-space box actually occupied by the live particles of one emitter, quads
        /// included. Each quad is taken at its DIAGONAL, because a particle with rotation sits
        /// at any angle and its corner is what has to be inside the marker.
        /// </summary>
        private bool TryMeasureParticles(GameObject root, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool any = false;

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.particleCount > _buffer.Length)
                    _buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(ps.particleCount)];

                int count = ps.GetParticles(_buffer);
                for (int i = 0; i < count; i++)
                {
                    Vector3 position = _buffer[i].position;
                    if (ps.main.simulationSpace == ParticleSystemSimulationSpace.Local)
                        position = ps.transform.TransformPoint(position);

                    Vector3 size = _buffer[i].GetCurrentSize3D(ps);
                    float half = 0.5f * Mathf.Sqrt((size.x * size.x) + (size.y * size.y));
                    var quad = new Bounds(position, new Vector3(half * 2f, half * 2f, 0f));

                    if (!any) { bounds = quad; any = true; }
                    else bounds.Encapsulate(quad);
                }
            }

            return any;
        }

        /// <summary>How far outside <paramref name="footprint"/> the particles reach, in world
        /// units. Zero or below means the marker contains them.</summary>
        private static float Shortfall(ParticleFootprint footprint, Vector3 origin, Bounds particles)
        {
            float left = (footprint.Min.x + origin.x) - particles.min.x;
            float right = particles.max.x - (footprint.Max.x + origin.x);
            float bottom = (footprint.Min.y + origin.y) - particles.min.y;
            float top = particles.max.y - (footprint.Max.y + origin.y);
            return Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));
        }

        private ParticleEmitter Run(ParticlePresetDefinition preset, float scale, float seconds)
        {
            var go = new GameObject("CoverageProbe");
            _created.Add(go);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scale);
            ParticleTestDeterminism.PinRandomness(go);

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                // Outside play mode a system that has never played swallows Simulate.
                ps.Play();
                ps.Simulate(seconds, true, false, true);
            }

            return emitter;
        }

        // ── The guarantee ────────────────────────────────────────────────────────

        [Test]
        public void EveryPreset_DrawnMarkerContainsEveryParticleItHasOnScreen()
        {
            var failures = new StringBuilder();
            int samples = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                // Lightning draws a LineRenderer and never builds a ParticleSystem.
                if (preset.vfx.kind == "lightning") continue;

                foreach (float scale in Scales)
                foreach (float seconds in SimulationTimes)
                {
                    var emitter = Run(preset, scale, seconds);
                    var probe = emitter.gameObject;

                    Bounds particles;
                    if (!TryMeasureParticles(probe, out particles)) continue;
                    samples++;

                    var drawn = ParticleFootprint.OfLive(emitter);
                    float gap = Shortfall(drawn, emitter.transform.position, particles);

                    if (gap > 0f)
                        failures.Append($"'{preset.id}' x{scale} at t={seconds}s: particles reach " +
                                        $"{gap:F3} u outside the drawn marker " +
                                        $"({drawn.HalfWidth * 2f:F2} x {drawn.HalfHeight * 2f:F2} " +
                                        $"vs particles {particles.size.x:F2} x {particles.size.y:F2}).\n");

                    // Freed eagerly, and the reference is taken BEFORE the destroy: reading
                    // emitter.gameObject afterwards throws MissingReferenceException. The
                    // whole catalog at two scales is several hundred live ParticleSystems,
                    // and a fixture that holds them all spends its run swapping.
                    _created.Remove(probe);
                    Object.DestroyImmediate(probe);
                }
            }

            Assert.Greater(samples, 100, "The sweep measured almost nothing — the catalog or " +
                                         "the simulation harness is broken, not the footprints.");
            Assert.IsEmpty(failures.ToString(),
                $"The F1 outline is drawn from ParticleFootprint.OfLive, so anything outside it " +
                $"is a particle outside its own marker — and the marker is also the click " +
                $"target.\n{failures}");
        }

        [Test]
        public void EveryPreset_UnclippedPredictionAlsoContainsThoseParticles()
        {
            var failures = new StringBuilder();
            int samples = 0, clipped = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (preset.vfx.kind == "lightning") continue;

                foreach (float scale in Scales)
                foreach (float seconds in SimulationTimes)
                {
                    var emitter = Run(preset, scale, seconds);
                    var probe = emitter.gameObject;

                    Bounds particles;
                    if (!TryMeasureParticles(probe, out particles)) continue;

                    var predicted = ParticleFootprint.Of(preset, scale);
                    if (predicted.Clipped) clipped++;
                    else
                    {
                        samples++;
                        float gap = Shortfall(predicted, emitter.transform.position, particles);
                        if (gap > 0f)
                            failures.Append($"'{preset.id}' x{scale} at t={seconds}s: particles reach " +
                                            $"{gap:F3} u outside the PREDICTED marker.\n");
                    }

                    _created.Remove(probe);
                    Object.DestroyImmediate(probe);
                }
            }

            Assert.Greater(samples, 100, "Nearly every prediction was clipped — the cap is too " +
                                         "low to bound anything, and this guard now checks nothing.");
            Assert.IsEmpty(failures.ToString(),
                "A prediction stands in for the frames before any particle exists. It has to " +
                "over-cover, never under-cover — an under-covering prediction is a marker the " +
                $"author sees particles escape from the moment an emitter is placed.\n{failures}");
        }

        [Test]
        public void MeasurementsAreNeverClipped_OnlyPredictionsAre()
        {
            // arcane_flame_emitter really does spread over 160 units. Clamping a MEASUREMENT
            // to the handle cap would draw a box its own particles left long ago.
            var preset = LoadCatalog().GetById("arcane_flame_emitter");
            Assert.IsTrue(preset != null, "'arcane_flame_emitter' is missing from the catalog.");

            var emitter = Run(preset, 1f, 7f);

            Bounds particles;
            Assert.IsTrue(TryMeasureParticles(emitter.gameObject, out particles),
                "The probe emitted nothing.");

            var drawn = ParticleFootprint.OfLive(emitter);

            Assert.Greater(drawn.Radius, ParticleFootprint.MaxHalfExtent,
                "This preset is larger than the prediction cap, which is exactly why the cap " +
                "must not apply to what was measured.");
            Assert.IsFalse(drawn.Clipped);
            Assert.LessOrEqual(Shortfall(drawn, emitter.transform.position, particles), 0f);
        }
    }
}
