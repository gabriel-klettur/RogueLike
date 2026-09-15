using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// Covers the <see cref="ParticleEmitter.StartEmitting"/> / <c>StopEmitting</c>
    /// pair used by long-lived togglers — currently
    /// <see cref="Valkur.Gameplay.ManaRegenAura"/> — that need to flip the
    /// underlying ParticleSystem on and off without rebuilding it.
    ///
    /// Mirrors the EditMode pattern used by ParticleEmitterReactivationTests:
    /// drive emission with <c>ParticleSystem.Simulate</c> and assert on
    /// <c>particleCount</c> rather than <c>isPlaying</c> (the latter is
    /// unreliable in EditMode without a frame loop).
    /// </summary>
    [TestFixture]
    public class ParticleEmitterStartStopTests
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

        private ParticleEmitter CreateLoopingEmitter()
        {
            var go = new GameObject("StartStopTestEmitter");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(MakeLoopingPreset(), 1f);
            return emitter;
        }

        private static ParticlePresetDefinition MakeLoopingPreset()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = "test_start_stop_loop";
            def.vfx = new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                emitRate = 20f,
                lifespan = 1f,
                speed = 1f,
                sizeMin = 0.1f,
                sizeMax = 0.3f,
            };
            return def;
        }

        private static ParticleSystem GetPs(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>();

        private static int CountAfterSimulate(ParticleSystem ps, float seconds = 0.5f)
        {
            ps.Simulate(seconds, withChildren: true, restart: false);
            return ps.particleCount;
        }

        [Test]
        public void StartEmitting_AfterStopEmitting_ResumesParticleEmission()
        {
            var emitter = CreateLoopingEmitter();
            var ps = GetPs(emitter);
            Assert.IsNotNull(ps);

            emitter.StopEmitting();
            // Clear the buffer so any in-flight particles don't survive.
            ps.Clear(true);
            Assert.AreEqual(0, CountAfterSimulate(ps),
                "Sanity: stopped + cleared system must emit 0 particles on Simulate.");

            emitter.StartEmitting();

            int after = CountAfterSimulate(ps);
            Assert.Greater(after, 0,
                "StartEmitting must re-Play() the underlying ParticleSystem so emission resumes.");
        }

        [Test]
        public void StartEmitting_TwiceInARow_IsIdempotent()
        {
            var emitter = CreateLoopingEmitter();

            Assert.DoesNotThrow(() =>
            {
                emitter.StartEmitting();
                emitter.StartEmitting();
            }, "StartEmitting must tolerate being called repeatedly without throwing.");
        }

        [Test]
        public void StartEmitting_BeforeApplyPreset_IsSafeNoOp()
        {
            var go = new GameObject("EmitterNoPreset_StartStop");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();

            Assert.DoesNotThrow(() => emitter.StartEmitting(),
                "StartEmitting must be a safe no-op before any ApplyPreset call.");
        }
    }
}
