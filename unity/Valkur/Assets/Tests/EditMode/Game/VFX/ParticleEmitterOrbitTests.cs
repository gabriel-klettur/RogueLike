using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers the fields that turn a swarm of billboards into a vortex: orbital velocity,
    /// radial pull, one-way spin and the authored emission fill.
    ///
    /// These exist because a portal mouth cannot be built out of the older fields. `speed`
    /// throws every particle straight out along its own spawn direction, so a circle of
    /// them is a starburst and never a swirl; `radiusThickness` was hard-coded per kind, so
    /// an aura could only ever ring its circle; and `rotationSpeedDegrees` randomised its
    /// sign per particle, which cancels the spin of any effect drawn by a couple of
    /// overlapping long-lived quads instead of by a crowd.
    ///
    /// The one measurement worth writing down: Unity's orbital velocity is ANGULAR and in
    /// RADIANS per second — an orbitalZ of 1 turns a particle 57.296° in one second, at any
    /// radius — while `radial` is linear world units per second. The emitter converts
    /// degrees to radians and scales only the linear term.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterOrbitTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

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

        // ── Fixtures ─────────────────────────────────────────────────────────────

        private ParticleEmitter CreateEmitter()
        {
            var go = new GameObject("OrbitTestEmitter");
            _created.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        private static ParticlePresetDefinition Aura(System.Action<ParticleVfxParams> tune = null)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = "orbit_test_aura";
            def.displayName = def.id;
            def.type = "aura";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = 20f,
                lifespan = 1f,
                speed = 0f,
                sizeMin = 0.05f,
                sizeMax = 0.1f,
                radius = 0.5f,
            };
            tune?.Invoke(def.vfx);
            return def;
        }

        private static ParticleSystem Ps(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>(true);

        // ── Orbit and pull ───────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_OrbitalSpeed_IsWrittenInRadiansPerSecond()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => v.orbitalSpeedDegrees = 180f));

            var vel = Ps(emitter).velocityOverLifetime;

            Assert.IsTrue(vel.enabled, "Authoring an orbit must enable velocityOverLifetime.");
            Assert.AreEqual(Mathf.PI, vel.orbitalZ.constant, 1e-4f,
                "Unity's orbital velocity is angular and in radians/s: 180 authored degrees is π.");
        }

        [Test]
        public void ApplyPreset_OrbitalSpeed_IsNotScaledByTheEmitterScale()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => v.orbitalSpeedDegrees = 90f), 4f);

            Assert.AreEqual(90f * Mathf.Deg2Rad, Ps(emitter).velocityOverLifetime.orbitalZ.constant, 1e-4f,
                "An angular rate is already size-independent — scaling it would spin a preset " +
                "faster the bigger it is placed.");
        }

        [Test]
        public void ApplyPreset_RadialSpeed_IsLinearAndScaled()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => v.radialSpeed = -0.25f), 2f);

            Assert.AreEqual(-0.5f, Ps(emitter).velocityOverLifetime.radial.constant, 1e-4f,
                "Radial pull is world units/s and must scale with the emitter, so a preset " +
                "reaches its centre in the same time at any placed size.");
        }

        [Test]
        public void ApplyPreset_DriftAndOrbit_ShareOneVelocityModule()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v =>
            {
                v.useGravityVector = true;
                v.gravityVector = new Vector2(0f, 0.4f);
                v.orbitalSpeedDegrees = 60f;
                v.radialSpeed = -0.3f;
            }));

            var vel = Ps(emitter).velocityOverLifetime;

            // All three live on the same module; writing it twice would mean the second
            // write silently dropping the first one's contribution.
            Assert.AreEqual(0.4f, vel.y.constant, 1e-4f, "Drift lost.");
            Assert.AreEqual(60f * Mathf.Deg2Rad, vel.orbitalZ.constant, 1e-4f, "Orbit lost.");
            Assert.AreEqual(-0.3f, vel.radial.constant, 1e-4f, "Radial pull lost.");
        }

        [Test]
        public void ApplyPreset_NoOrbitNoDrift_LeavesTheVelocityModuleOff()
        {
            var emitter = CreateEmitter();
            // Reused emitters are the norm — the F1 preview emitter serves every preset the
            // author clicks — so a module one preset turns on must be turned off by the next.
            emitter.ApplyPreset(Aura(v => { v.orbitalSpeedDegrees = 200f; v.radialSpeed = -1f; }));
            emitter.ApplyPreset(Aura());

            Assert.IsFalse(Ps(emitter).velocityOverLifetime.enabled,
                "A preset with no drift, orbit or pull must switch the module back off.");
        }

        [Test]
        public void OrbitAndPull_MoveAParticleAroundAndInward()
        {
            // The measurement the conversions above are derived from, run end to end: one
            // second at 90°/s and -0.25 u/s from a start radius of 1.
            var go = new GameObject("OrbitProbe");
            _created.Add(go);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 10f;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.gravityModifier = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(90f * Mathf.Deg2Rad);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.25f);

            // Play() before Emit(): outside play mode a ParticleSystem that has never played
            // swallows Emit silently — particleCount stays 0 and the test measures nothing.
            ps.Play();

            // startLifetime and startSize are set on the EmitParams as well as on the module:
            // an EmitParams left to its defaults carries a zero lifetime, and the particle is
            // reaped before Simulate ever advances it.
            var ep = new ParticleSystem.EmitParams
            {
                position = new Vector3(1f, 0f, 0f),
                velocity = Vector3.zero,
                startLifetime = 10f,
                startSize = 0.1f,
            };
            ps.Emit(ep, 1);
            ps.Simulate(1f, true, false, true);

            var buffer = new ParticleSystem.Particle[4];
            Assert.AreEqual(1, ps.GetParticles(buffer), "The probe particle did not survive.");

            var pos = buffer[0].position;
            Assert.AreEqual(90f, Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg, 0.5f,
                "90°/s for one second is a quarter turn.");
            Assert.AreEqual(0.75f, new Vector2(pos.x, pos.y).magnitude, 1e-3f,
                "-0.25 u/s for one second pulls the particle a quarter of the way in.");
        }

        // ── One-way spin ─────────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_RotationOneWay_FixesTheSignInsteadOfRandomisingIt()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => { v.rotationSpeedDegrees = -45f; v.rotationOneWay = true; }));

            var rot = Ps(emitter).rotationOverLifetime;

            Assert.IsTrue(rot.enabled);
            Assert.AreEqual(-45f * Mathf.Deg2Rad, rot.z.constant, 1e-4f,
                "A shape that IS the effect — a Vortex gate drawn by overlapping quads — " +
                "needs every copy turning the same way, or the spin cancels into a flicker.");
        }

        [Test]
        public void ApplyPreset_RotationDefault_StaysBidirectional()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => v.rotationSpeedDegrees = 45f));

            var rot = Ps(emitter).rotationOverLifetime;
            float rad = 45f * Mathf.Deg2Rad;

            Assert.AreEqual(-rad, rot.z.constantMin, 1e-4f);
            Assert.AreEqual(rad, rot.z.constantMax, 1e-4f);
        }

        // ── Emission fill ────────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_ShapeFillUnset_KeepsTheKindsOwnChoice()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura());

            Assert.AreEqual(0f, Ps(emitter).shape.radiusThickness, 1e-4f,
                "aura has always emitted from the rim; -1 must not change that.");
        }

        [Test]
        public void ApplyPreset_ShapeFill_OverridesTheKind()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => v.shapeFill = 1f));

            Assert.AreEqual(1f, Ps(emitter).shape.radiusThickness, 1e-4f,
                "An authored fill of 1 must turn the aura's rim into a filled disc.");
        }

        // ── Emission rate floor ──────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_SubOnePerSecondRate_SurvivesInsteadOfBeingFlooredAtOne()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Aura(v => { v.emitRate = 0.25f; v.lifespan = 12f; }));

            Assert.AreEqual(0.25f, Ps(emitter).emission.rateOverTime.constant, 1e-4f,
                "The old 1/s floor quadrupled the live population of any preset whose whole " +
                "effect is a couple of long-lived quads.");
        }
    }
}
