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
    /// A RESIZE MUST NEVER STOP THE PARTICLES MOVING.
    ///
    /// The reported symptom, third time around: shrink a placed emitter's boxes past a point
    /// and "the leaves still appear and disappear, but they stop falling". Two separate
    /// mechanisms produced it, and both are pinned here.
    ///
    ///  1. THE REACH RATIO SCALES MOTION, and it is allowed down to a twentieth. Measured on
    ///     the shipped leaf field, reach 0.05 takes the drift from 0.55 u/s to 0.0275 — nine
    ///     TENTHS of a pixel over a two-second life at 16 PPU. Spawning and dying carry on
    ///     untouched, so the field reads as broken rather than as small. The drag now stops at
    ///     a floor and says why.
    ///  2. THE LIVE PATH DIVERGED FROM THE REBUILD. ApplyGeometry did not rewrite
    ///     limitVelocityOverLifetime, whose LIMIT is derived from `speed` — which reach
    ///     scales. Measured across the catalog that was 81 systems configured one way while
    ///     being dragged and another way after a reload.
    ///
    ///  3. MOTION DOES NOT ONLY COME FROM THE REACH RATIO. An orbit sweeps ground in
    ///     proportion to the radius it turns around, so collapsing the EMISSION box of the
    ///     portal's inflow layer slowed its particles to a quarter of a pixel per second while
    ///     the reach ratio sat untouched at 1. The floor is therefore applied to whichever box
    ///     is being dragged, by asking the only question that matters: how far does a particle
    ///     get over its life.
    ///
    /// Motion is measured as the mean displacement of a COHORT emitted through the preset's own
    /// shape, compared with itself by index. Counting particles proves only that they exist,
    /// which is exactly what was already true while the effect was frozen; and
    /// <c>Particle.velocity</c> excludes the velocity-over-lifetime module, which is where a
    /// preset's drift lives, so the honest measurement is displacement.
    /// </summary>
    [TestFixture]
    public class ParticleResizeMotionTests
    {
        private const string CATALOG_PATH =
            "Assets/_Project/Data/Catalogs/Particles/ParticlePresetCatalog.asset";

        /// <summary>Particles emitted per measurement. Enough that one unlucky draw from a
        /// randomised start speed cannot decide the result.</summary>
        private const int COHORT = 64;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly ParticleSystem.Particle[] _before = new ParticleSystem.Particle[256];
        private readonly ParticleSystem.Particle[] _after = new ParticleSystem.Particle[256];

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

        /// <summary>
        /// Mean distance this preset's particles cover in <paramref name="seconds"/>, with the
        /// given overrides in force.
        ///
        /// Emitted THROUGH the shape (<c>ps.Emit(count)</c>), because that is the only way the
        /// start speed a preset relies on is applied — an earlier version of this harness
        /// injected particles with an explicit zero velocity and reported that half the catalog
        /// never moved, which was the harness and not the effect. Emission is then switched off
        /// so the population is a fixed cohort, and the same particle is compared with itself
        /// by index across the window.
        /// </summary>
        private float TravelOf(ParticlePresetDefinition preset, ParticleInstanceOverrides overrides,
                               float seconds, bool liveResize)
        {
            var go = new GameObject("MotionProbe_" + preset.id);
            _created.Add(go);

            var emitter = go.AddComponent<ParticleEmitter>();
            if (liveResize)
            {
                // The editor's path: build at the preset's own size, then resize a running
                // system the way a drag does.
                emitter.ApplyPreset(preset, 1f);
                foreach (var warm in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    warm.Play();
                    warm.Simulate(1f, true, false, true);
                }
                emitter.SetOverrides(overrides);
            }
            else
            {
                emitter.ApplyPreset(preset, 1f, overrides);
            }

            var ps = go.GetComponentInChildren<ParticleSystem>(true);
            if (ps == null) return 0f;

            var emission = ps.emission;
            emission.enabled = false;
            ps.Clear();
            ps.Play();
            ps.Emit(COHORT);

            int before = ps.GetParticles(_before);
            ps.Simulate(seconds, true, false, true);
            int after = ps.GetParticles(_after);

            int count = Mathf.Min(before, after);
            if (count <= 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < count; i++)
                sum += (_after[i].position - _before[i].position).magnitude;
            return sum / count;
        }

        /// <summary>
        /// Squashes a preset by dragging every edge of both boxes as far inward as the handles
        /// allow — the gesture a hand makes when it wants an effect as small as possible.
        /// </summary>
        private static ParticleInstanceOverrides SquashByDragging(ParticlePresetDefinition preset)
        {
            var overrides = ParticleInstanceOverrides.None;
            var edges = new[]
            {
                ParticleBoundsEdge.Right, ParticleBoundsEdge.Top,
                ParticleBoundsEdge.Left, ParticleBoundsEdge.Bottom,
            };

            // Several passes: each axis constrains the other through the shared motion floor,
            // so one sweep of the four edges does not reach the limit.
            for (int pass = 0; pass < 8; pass++)
            {
                foreach (var edge in edges)
                {
                    var emission = ParticleBoundsHandles.DragEmissionEdge(
                        preset, 1f, overrides, edge, Vector2.zero, Vector2.zero,
                        symmetric: true, snap: 0f);
                    if (emission.Changed) overrides = emission.Overrides;

                    var reach = ParticleBoundsHandles.DragReachEdge(
                        preset, 1f, overrides, edge, Vector2.zero, Vector2.zero, snap: 0f);
                    if (reach.Changed) overrides = reach.Overrides;
                }
            }

            return overrides;
        }

        // ── The floor ────────────────────────────────────────────────────────────

        [Test]
        public void TheReachDrag_StopsBeforeItFreezesTheEffect()
        {
            var preset = Shipped("falling_leaf_30s");

            // Pull the reach box's bottom edge far inside the emitter — as deep as a hand can
            // drag it.
            var drag = ParticleBoundsHandles.DragReachEdge(
                preset, 1f, ParticleInstanceOverrides.None,
                ParticleBoundsEdge.Bottom, Vector2.zero, new Vector2(0f, -0.05f), snap: 0f);

            Assert.IsTrue(drag.Changed);
            Assert.IsTrue(drag.StoppedAtMotionFloor,
                "The editor needs this flag to explain the stop; a drag that silently ignores " +
                "the cursor reads as a broken handle.");
            Assert.Greater(drag.Overrides.reachScale, ParticleInstanceOverrides.MinRatio,
                "The authoring minimum is a twentieth, at which a leaf covers under a pixel " +
                "over its whole life.");
        }

        [Test]
        public void AtTheFloor_ALeafStillCoversSeveralPixels()
        {
            var preset = Shipped("falling_leaf_30s");
            float floor = ParticleBoundsHandles.MinVisibleReachRatio(
                preset, 1f, ParticleInstanceOverrides.None);

            float travel = TravelOf(preset, new ParticleInstanceOverrides(1f, 1f, floor),
                                    preset.vfx.lifespan, liveResize: false);

            Assert.GreaterOrEqual(travel, ParticleBoundsHandles.MinVisibleLifetimeTravel * 0.8f,
                $"At the floor ratio ({floor:0.###}) a leaf travels {travel:0.###} u over its " +
                "life — under the four texels that separate a slow field from a still one.");
        }

        [Test]
        public void APresetWhoseParticlesBarelyMove_IsNotClampedForNoReason()
        {
            // Nothing to protect: a preset that hardly travels at ratio 1 has no stillness to
            // be dragged into, and clamping it would make its reach box unresizable.
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(new GameObject("holder"));   // keeps TearDown symmetrical
            def.id = "still_preset";
            def.displayName = def.id;
            def.vfx = new ParticleVfxParams
            {
                kind = "aura", loops = true, radius = 0.5f, directionDegrees = -1f,
                speed = 0.02f, lifespan = 1f, sizeMin = 0f, sizeMax = 0f,
                gravity = 0f, useGravityVector = false, noiseEnabled = false, swayAmp = 0f,
            };
            def.layers = new List<ParticlePresetDefinition>();

            Assert.AreEqual(ParticleInstanceOverrides.MinRatio,
                ParticleBoundsHandles.MinVisibleReachRatio(def, 1f, ParticleInstanceOverrides.None),
                1e-4f);

            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheFloorIsRelativeAsWellAsAbsolute_SoAFastEffectCannotBeSlowedPastRecognition()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = "fast_preset";
            def.displayName = def.id;
            def.vfx = new ParticleVfxParams
            {
                kind = "aura", loops = true, radius = 0.5f, directionDegrees = -1f,
                speed = 10f, lifespan = 1f, sizeMin = 0f, sizeMax = 0f,
                useGravityVector = false, noiseEnabled = false, swayAmp = 0f,
            };
            def.layers = new List<ParticlePresetDefinition>();

            // Ten units of travel: the absolute floor alone would allow a fortieth of the
            // authored speed, which is a stalled effect long before four texels are in danger.
            Assert.AreEqual(ParticleBoundsHandles.MinVisibleTravelFraction,
                ParticleBoundsHandles.MinVisibleReachRatio(def, 1f, ParticleInstanceOverrides.None),
                1e-4f);

            Object.DestroyImmediate(def);
        }

        // ── Motion survives every resize the editor can author ───────────────────

        [Test]
        public void NoPresetCanBeDraggedIntoStillness()
        {
            // THE USER-FACING GUARANTEE. Every preset the guard protects is squashed as far as
            // both boxes will go, through the real handle arithmetic, and then measured: its
            // particles must still cover the distance the guard promises.
            var failures = new StringBuilder();
            int guarded = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (!preset.vfx.loops || preset.vfx.kind == "lightning") continue;

                float floor = ParticleBoundsHandles.VisibleTravelFloor(preset, 1f);
                if (floor <= 0f) continue;      // authored static: nothing to take away
                guarded++;

                var squashed = SquashByDragging(preset);

                float life = Mathf.Max(0.05f, preset.vfx.lifespan);
                float window = Mathf.Min(life * 0.9f, 3f);
                float travelled = TravelOf(preset, squashed, window, liveResize: true);
                float promised = floor * (window / life);

                if (travelled < promised)
                    failures.Append($"'{preset.id}' squashed to {squashed}: particles cover " +
                                    $"{travelled:0.####} u in {window:0.##} s, under the " +
                                    $"{promised:0.####} the motion floor promises.\n");

                foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
                _created.Clear();
            }

            Assert.Greater(guarded, 50, "Almost nothing is being guarded — the floor is not " +
                                        "engaging and this test is checking nothing.");
            Assert.IsEmpty(failures.ToString(),
                "Shrinking an emitter as far as the editor allows must never leave its " +
                $"particles standing still.\n{failures}");
        }

        [Test]
        public void TheTravelEstimate_IsALowerBoundOnWhatTheParticlesActuallyDo()
        {
            // The floor is only as trustworthy as the estimate under it. If LifetimeTravel ever
            // reports MORE than the particles cover, the guard lets a drag stop an effect while
            // believing it still moves — which is precisely how the orbital inflow slipped
            // through: crediting a full radial pull where the geometry gave it a fiftieth of
            // the radius to pull across.
            var overrideSets = new[]
            {
                ParticleInstanceOverrides.None,
                new ParticleInstanceOverrides(0.2f, 0.2f, 0.5f),
                new ParticleInstanceOverrides(0.05f, 0.05f, 0.25f),
            };

            var failures = new StringBuilder();
            int samples = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (!preset.vfx.loops || preset.vfx.kind == "lightning") continue;

                foreach (var overrides in overrideSets)
                {
                    float life = Mathf.Max(0.05f, preset.vfx.lifespan);
                    float window = Mathf.Min(life * 0.9f, 3f);
                    float estimate = ParticleFootprint.LifetimeTravel(preset, 1f, overrides)
                                     * (window / life);
                    if (estimate <= 1e-4f) continue;

                    samples++;

                    // Best of two runs. Noise and the randomised start speed make this a
                    // sample of a distribution, and the risk to guard against is an unusually
                    // SLOW draw being read as the estimate over-claiming.
                    float measured = Mathf.Max(
                        TravelOf(preset, overrides, window, liveResize: false),
                        TravelOf(preset, overrides, window, liveResize: false));

                    if (measured < estimate)
                        failures.Append($"'{preset.id}' {overrides}: estimate {estimate:0.####} u " +
                                        $"over {window:0.##} s, particles covered {measured:0.####}.\n");

                    foreach (var go in _created) if (go != null) Object.DestroyImmediate(go);
                    _created.Clear();
                }
            }

            Assert.Greater(samples, 100, "The sweep measured almost nothing.");
            Assert.IsEmpty(failures.ToString(),
                "LifetimeTravel is a BOUND, not a guess: it must never claim more motion than " +
                $"the running systems produce.\n{failures}");
        }

        [Test]
        public void ShrinkingAnOrbitsEmissionBox_IsGuardedToo()
        {
            // Not every freeze comes from the reach ratio. An orbit covers ground in proportion
            // to the radius it turns around, so collapsing the EMISSION box of the portal's
            // inflow layer slows its particles just as surely — measured, a twentieth of the
            // radius took them to a quarter of a pixel per second.
            var preset = Shipped("portal_oval_inflow");

            var squashed = SquashByDragging(preset);

            Assert.Greater(squashed.spawnScaleX * squashed.spawnScaleY,
                           ParticleInstanceOverrides.MinRatio * ParticleInstanceOverrides.MinRatio * 4f,
                "Both axes were driven to the authoring minimum, which for an orbit means no " +
                "radius left to sweep.");

            float life = Mathf.Max(0.05f, preset.vfx.lifespan);
            float travelled = TravelOf(preset, squashed, life * 0.9f, liveResize: true);
            float promised = ParticleBoundsHandles.VisibleTravelFloor(preset, 1f) * 0.9f;

            Assert.GreaterOrEqual(travelled, promised);
        }

        [Test]
        public void TheFirstPixelOfADrag_ChangesNothingButTheSize()
        {
            // A resize handle must be continuous at the point it is touched. It was not for the
            // two strip kinds: they emit from a box ConfigureShape hard-codes, and the moment an
            // override went non-default ParticleOverrideApplier materialised that box into
            // spawnWidth/spawnHeight — at which point a DIFFERENT branch built it, aimed
            // upward instead of along the camera axis and multiplying the height by the
            // instance scale. Touching the handle moved the effect before the drag had asked
            // for anything.
            var failures = new StringBuilder();

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (preset.vfx.kind == "lightning") continue;

                foreach (float scale in new[] { 1f, 2f })
                {
                    var untouched = BuildShape(preset, scale, ParticleInstanceOverrides.None);
                    // One thousandth of a ratio: the smallest thing a drag can do.
                    var nudged = BuildShape(preset, scale,
                        new ParticleInstanceOverrides(0.999f, 0.999f, 1f));

                    if (Vector3.Distance(untouched.scale, nudged.scale) > untouched.scale.magnitude * 0.02f + 1e-3f)
                        failures.Append($"'{preset.id}' x{scale}: emission box jumps from " +
                                        $"{untouched.scale:F3} to {nudged.scale:F3} on the first " +
                                        "pixel of a drag.\n");

                    if (Quaternion.Angle(Quaternion.Euler(untouched.rotation),
                                         Quaternion.Euler(nudged.rotation)) > 1f)
                        failures.Append($"'{preset.id}' x{scale}: emission box ROTATES from " +
                                        $"{untouched.rotation:F0} to {nudged.rotation:F0} on the " +
                                        "first pixel of a drag — the throw direction moves with it.\n");
                }
            }

            Assert.IsEmpty(failures.ToString(),
                $"A handle has to be continuous where it is grabbed.\n{failures}");
        }

        /// <summary>Shape module state a preset builds at a given size.</summary>
        private (Vector3 scale, Vector3 rotation) BuildShape(
            ParticlePresetDefinition preset, float scale, ParticleInstanceOverrides overrides)
        {
            var go = new GameObject("ShapeProbe");
            _created.Add(go);

            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(preset, scale, overrides);

            var ps = go.GetComponentInChildren<ParticleSystem>(true);
            var result = ps == null
                ? (Vector3.zero, Vector3.zero)
                : (ps.shape.scale, ps.shape.rotation);

            Object.DestroyImmediate(go);
            _created.Remove(go);
            return result;
        }

        // ── The live path and the rebuild must agree ─────────────────────────────

        /// <summary>Every module state a resize can touch, as one comparable string.</summary>
        private static string Snapshot(ParticleSystem ps)
        {
            var m = ps.main;
            var v = ps.velocityOverLifetime;
            var sh = ps.shape;
            var n = ps.noise;
            var lim = ps.limitVelocityOverLifetime;
            var em = ps.emission;

            return string.Join("|", new[]
            {
                "speed=" + m.startSpeed.constantMax.ToString("F4"),
                "grav=" + m.gravityModifier.constant.ToString("F4"),
                "life=" + m.startLifetime.constant.ToString("F3"),
                "vel=" + v.enabled + "," + v.x.constant.ToString("F4") + "," + v.y.constant.ToString("F4")
                       + "," + v.orbitalZ.constant.ToString("F4") + "," + v.radial.constant.ToString("F4"),
                "shape=" + sh.shapeType + "," + sh.scale.ToString("F3") + "," + sh.radius.ToString("F3")
                         + "," + sh.radiusThickness.ToString("F2") + "," + sh.rotation.ToString("F0"),
                "noise=" + n.enabled + "," + n.strength.constant.ToString("F4") + ","
                         + n.frequency.ToString("F3") + "," + n.separateAxes + ","
                         + n.strengthY.constant.ToString("F4"),
                "lim=" + lim.enabled + "," + lim.dampen.ToString("F3") + ","
                       + lim.limit.constant.ToString("F4"),
                "rate=" + em.rateOverTime.constant.ToString("F3"),
            });
        }

        [Test]
        public void TheLiveResizePath_ConfiguresExactlyWhatARebuildWould()
        {
            var overrideSets = new[]
            {
                ParticleInstanceOverrides.None,
                new ParticleInstanceOverrides(0.05f, 0.05f, 0.05f),
                new ParticleInstanceOverrides(0.3f, 2f, 0.5f),
                new ParticleInstanceOverrides(4f, 1f, 3f),
            };

            var failures = new StringBuilder();
            int compared = 0;

            foreach (var preset in LoadCatalog().Presets)
            {
                if (preset == null || preset.vfx == null) continue;
                if (preset.vfx.kind == "lightning") continue;

                foreach (var overrides in overrideSets)
                {
                    var liveGo = new GameObject("Live");
                    var live = liveGo.AddComponent<ParticleEmitter>();
                    live.ApplyPreset(preset, 1f);
                    live.SetOverrides(overrides);

                    var builtGo = new GameObject("Rebuilt");
                    var built = builtGo.AddComponent<ParticleEmitter>();
                    built.ApplyPreset(preset, 1f, overrides);

                    var a = liveGo.GetComponentsInChildren<ParticleSystem>(true);
                    var b = builtGo.GetComponentsInChildren<ParticleSystem>(true);

                    if (a.Length != b.Length)
                    {
                        failures.Append($"'{preset.id}' {overrides}: {a.Length} live systems vs " +
                                        $"{b.Length} rebuilt.\n");
                    }
                    else
                    {
                        for (int i = 0; i < a.Length; i++)
                        {
                            compared++;
                            string sa = Snapshot(a[i]);
                            string sb = Snapshot(b[i]);
                            if (sa == sb) continue;

                            var pa = sa.Split('|');
                            var pb = sb.Split('|');
                            for (int k = 0; k < pa.Length; k++)
                                if (pa[k] != pb[k])
                                    failures.Append($"'{preset.id}' sys{i} {overrides}: " +
                                                    $"live {pa[k]} vs rebuilt {pb[k]}\n");
                        }
                    }

                    Object.DestroyImmediate(liveGo);
                    Object.DestroyImmediate(builtGo);
                }
            }

            Assert.Greater(compared, 300, "The sweep compared almost nothing.");
            Assert.IsEmpty(failures.ToString(),
                "A resized emitter has to be configured identically whether the size arrived " +
                "through a live drag or through the rebuild a reload performs. Anything here " +
                "is a module the live path forgot, and it will behave one way in the editor " +
                $"and another after a restart.\n{failures}");
        }
    }
}
