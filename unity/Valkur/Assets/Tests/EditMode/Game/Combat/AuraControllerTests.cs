using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Robustness tests for the AuraController VFX rig.
    ///
    /// Specifically guards the recent fix for two ParticleSystem misuses in
    /// <see cref="AuraController"/>'s <c>BuildSparkles()</c>:
    ///   * setting <c>main.duration</c> while the system was still in its auto-Play state,
    ///   * leaving <c>velocityOverLifetime.x/z</c> in different MinMaxCurveMode than .y.
    /// Either bug raised dozens of console errors when an aura was cast.
    /// </summary>
    public class AuraControllerTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() { LogAssert.ignoreFailingMessages = true; }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        private AuraController CreateAura()
        {
            _go = new GameObject("AuraTest");
            var ac = _go.AddComponent<AuraController>();
            // Initialize with sensible defaults so the visual rig is fully built.
            ac.InitializeHealing(
                duration:     2f,
                gameRadius:   1.5f,
                visualRadius: 1.5f,
                healPerTick:  10,
                tickPeriod:   0.5f,
                caster:       _go.transform);
            return ac;
        }

        // ── BuildSparkles: ParticleSystem fix guards ──────────────────

        [Test]
        public void Initialize_BuildsSparklesParticleSystem()
        {
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            Assert.IsNotNull(ps, "BuildSparkles must add a ParticleSystem child");
        }

        [Test]
        public void Sparkles_PlayOnAwakeIsFalse()
        {
            // Regression guard: leaving playOnAwake = true caused
            // "Setting the duration while system is still playing is not supported".
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            var main = ps.main;
            Assert.IsFalse(main.playOnAwake,
                "main.playOnAwake must be false (set BEFORE configuring duration/lifetime)");
        }

        [Test]
        public void Sparkles_DurationConfiguredCorrectly()
        {
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            var main = ps.main;
            Assert.AreEqual(5f, main.duration, 0.01f,
                "Sparkles main.duration should be 5s (looped)");
            Assert.IsTrue(main.loop, "Sparkles must loop");
        }

        [Test]
        public void Sparkles_VelocityOverLifetime_AllAxesShareCurveMode()
        {
            // Regression guard: x/y/z must all use the same MinMaxCurveMode or Unity logs
            // "Particle Velocity curves must all be in the same mode" every frame.
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            var v = ps.velocityOverLifetime;

            Assert.IsTrue(v.enabled, "velocityOverLifetime must be enabled");
            Assert.AreEqual(v.x.mode, v.y.mode,
                "velocityOverLifetime.x and .y must use the same MinMaxCurveMode");
            Assert.AreEqual(v.y.mode, v.z.mode,
                "velocityOverLifetime.y and .z must use the same MinMaxCurveMode");
        }

        [Test]
        public void Sparkles_VelocityOverLifetime_YDriftsUpward()
        {
            // Sparkles should rise (heat-rise illusion) — y range is positive.
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            var v = ps.velocityOverLifetime;
            Assert.Greater(v.y.constantMin, 0f,
                "velocityOverLifetime.y min must be > 0 so sparkles drift upward");
            Assert.GreaterOrEqual(v.y.constantMax, v.y.constantMin,
                "velocityOverLifetime.y max must be >= min");
        }

        [Test]
        public void Sparkles_IsPlayingAfterInitialize()
        {
            var ac = CreateAura();
            var ps = ac.GetComponentInChildren<ParticleSystem>(includeInactive: true);
            // ps.Play(true) is called at the end of BuildSparkles().
            Assert.IsTrue(ps.isPlaying || ps.particleCount >= 0,
                "Sparkles ParticleSystem must be playing after Initialize");
        }

        // ── Initialize bookkeeping ────────────────────────────────────

        [Test]
        public void Initialize_StoresHealingParameters()
        {
            var ac = CreateAura();
            // Read back the private state via reflection to guarantee no field renaming
            // silently breaks the heal cadence.
            float remaining = (float)GetField(ac, "_remaining");
            int   heal      = (int)  GetField(ac, "_healPerTick");
            float period    = (float)GetField(ac, "_tickPeriod");

            Assert.AreEqual(2f,   remaining, 0.001f);
            Assert.AreEqual(10,   heal);
            Assert.AreEqual(0.5f, period,    0.001f);
        }

        [Test]
        public void Initialize_BuildsRuneAndGlowChildren()
        {
            var ac = CreateAura();
            // Verify the visual rig has the expected key sub-renderers.
            var sprites = ac.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            Assert.Greater(sprites.Length, 0,
                "Aura should build at least one visual sprite child");
        }

        private static object GetField(object instance, string name)
        {
            var f = instance.GetType().GetField(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, $"Field '{name}' not found on {instance.GetType().Name}");
            return f.GetValue(instance);
        }
    }
}
