using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// Regression tests for the ParticleEmitter.OnEnable() fix.
    ///
    /// Bug history: ParticleInstancesLoader's viewport culling SetActive(false)/(true)
    /// flow left looping emitters in a "drawn but static" state because Unity does not
    /// auto-replay a ParticleSystem after a deactivate/reactivate cycle — main.loop=true
    /// only recycles emission *while playing*, it does not resume from a stopped state.
    ///
    /// The fix adds OnEnable() that calls _ps.Play() whenever the GO is reactivated
    /// (and the system isn't already playing). These tests guard that behaviour.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterReactivationTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private ParticleEmitter CreateLoopingEmitter()
        {
            var go = new GameObject("TestEmitter_Reactivation");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(MakeLoopingPreset(), 1f);
            return emitter;
        }

        private static ParticlePresetDefinition MakeLoopingPreset()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id  = "test_looping";
            def.vfx = new ParticleVfxParams
            {
                kind     = "aura",
                loops    = true,
                emitRate = 10f,
                lifespan = 1f,
                speed    = 1f,
                sizeMin  = 0.1f,
                sizeMax  = 0.3f,
            };
            return def;
        }

        private static ParticlePresetDefinition MakeFinitePreset()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id  = "test_finite";
            def.vfx = new ParticleVfxParams
            {
                kind     = "explosion",
                loops    = false,
                count    = 8,
                lifespan = 0.5f,
                speed    = 1f,
                sizeMin  = 0.1f,
                sizeMax  = 0.3f,
            };
            return def;
        }

        private static ParticleSystem GetPs(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>();

        // ── tests ─────────────────────────────────────────────────────────────────

        // Assertion helper: a "live" particle system in EditMode is one that
        // produces particles when Simulate ticks. ps.isPlaying is unreliable in
        // EditMode without a frame loop, so we measure emission instead.
        private static int CountAfterSimulate(ParticleSystem ps, float seconds = 0.5f)
        {
            ps.Simulate(seconds, withChildren: true, restart: false);
            return ps.particleCount;
        }

        [Test]
        public void ApplyPreset_LoopingPreset_EmitsParticles()
        {
            var emitter = CreateLoopingEmitter();
            var ps = GetPs(emitter);
            Assert.IsNotNull(ps, "ParticleSystem must be created by ApplyPreset.");

            int count = CountAfterSimulate(ps);
            Assert.Greater(count, 0,
                "Looping emitter must emit particles after ApplyPreset + Simulate tick.");
        }

        [Test]
        public void OnEnable_RestartsEmission_AfterStop_RegressionStaticParticles()
        {
            // Regression guard for the "drawn but static" bug:
            // ParticleInstancesLoader's viewport culling does SetActive(false→true)
            // shortly after spawn. Without our fix, Play() was never re-issued and
            // looping emitters appeared static after the cycle.
            //
            // EditMode does not reliably fire OnEnable through SetActive in the
            // way Unity does at runtime (Simulate cycles don't drive the enable
            // state machine), so we invoke OnEnable directly via reflection and
            // verify that emission resumes (proves Play() was called).
            var emitter = CreateLoopingEmitter();
            var ps = GetPs(emitter);

            // Stop the system as if culling froze it.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            int countWhileStopped = CountAfterSimulate(ps);
            Assert.AreEqual(0, countWhileStopped,
                "Sanity: stopped + cleared system must emit 0 on Simulate.");

            // Trigger OnEnable manually to mimic SetActive(false→true) lifecycle.
            InvokeOnEnable(emitter);

            int countAfter = CountAfterSimulate(ps);
            Assert.Greater(countAfter, 0,
                "OnEnable must restart emission so a culled-then-revealed looping "
                + "emitter does not appear static.");
        }

        [Test]
        public void OnEnable_OnFinitePreset_DoesNotForceLoop()
        {
            // OnEnable calls Play() on any preset, but loop semantics must still
            // come from the preset itself — finite presets stay finite.
            var go = new GameObject("FiniteEmitter_OnEnable");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(MakeFinitePreset(), 1f);
            var ps = GetPs(emitter);

            Assert.IsFalse(ps.main.loop, "Finite preset must not loop after ApplyPreset.");

            InvokeOnEnable(emitter);

            Assert.IsFalse(ps.main.loop,
                "OnEnable must not mutate main.loop on a finite preset.");
            Assert.AreEqual(ParticleSystemStopAction.Disable, ps.main.stopAction,
                "OnEnable must not mutate stopAction on a finite preset.");
        }

        private static void InvokeOnEnable(ParticleEmitter emitter)
        {
            var m = typeof(ParticleEmitter).GetMethod("OnEnable",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m,
                "ParticleEmitter must define a private OnEnable() — the fix relies on it.");
            m.Invoke(emitter, null);
        }

        [Test]
        public void OnEnable_BeforeApplyPreset_DoesNotThrow()
        {
            // OnEnable runs the moment the component is added. _ps and _preset
            // are still null — the guards must keep it a no-op.
            var go = new GameObject("EmitterNoPreset");
            _created.Add(go);

            Assert.DoesNotThrow(() =>
            {
                var emitter = go.AddComponent<ParticleEmitter>();
                go.SetActive(false);
                go.SetActive(true);
            }, "OnEnable must be a safe no-op when ApplyPreset has not run yet.");
        }

    }
}
