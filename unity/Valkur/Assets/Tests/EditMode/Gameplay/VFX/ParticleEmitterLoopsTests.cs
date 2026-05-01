using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Gameplay.VFX
{
    /// <summary>
    /// Verifies that <see cref="ParticleEmitter"/> uses <see cref="ParticleVfxParams.loops"/>
    /// as the single source of truth for loop/stopAction configuration.
    /// The kind field no longer drives looping behaviour — the attribute does.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterLoopsTests
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

        private ParticleEmitter CreateEmitter(string kind, bool loops)
        {
            var go = new GameObject($"TestEmitter_{kind}_loops{loops}");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            var preset  = MakePreset(kind, loops);
            emitter.ApplyPreset(preset, 1f);
            return emitter;
        }

        private static ParticlePresetDefinition MakePreset(string kind, bool loops)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id  = $"{kind}_loops{loops}";
            def.vfx = new ParticleVfxParams
            {
                kind     = kind,
                loops    = loops,
                count    = 10,
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

        [Test]
        public void ApplyPreset_LoopsTrue_BurstKind_StillLoops()
        {
            // kind=explosion normally implies finite, but loops=true overrides that.
            var emitter = CreateEmitter("explosion", loops: true);
            var ps      = GetPs(emitter);
            Assert.IsNotNull(ps);

            Assert.IsTrue(ps.main.loop,
                "loops=true must force main.loop=true even when kind='explosion'.");
            Assert.AreEqual(ParticleSystemStopAction.None, ps.main.stopAction,
                "loops=true must produce stopAction=None regardless of kind.");
        }

        [Test]
        public void ApplyPreset_LoopsFalse_ContinuousKind_DoesNotLoop()
        {
            // kind=aura normally implies continuous, but loops=false overrides that.
            var emitter = CreateEmitter("aura", loops: false);
            var ps      = GetPs(emitter);
            Assert.IsNotNull(ps);

            Assert.IsFalse(ps.main.loop,
                "loops=false must force main.loop=false even when kind='aura'.");
        }

        [Test]
        public void ApplyPreset_LoopsFalse_StopActionDisable()
        {
            var emitter = CreateEmitter("explosion", loops: false);
            var ps      = GetPs(emitter);
            Assert.IsNotNull(ps);

            Assert.AreEqual(ParticleSystemStopAction.Disable, ps.main.stopAction,
                "loops=false (one-shot) must configure stopAction=Disable so the GO " +
                "deactivates automatically after the burst completes.");
        }

        [Test]
        public void ApplyPreset_LoopsTrue_StopActionNone()
        {
            var emitter = CreateEmitter("aura", loops: true);
            var ps      = GetPs(emitter);
            Assert.IsNotNull(ps);

            Assert.AreEqual(ParticleSystemStopAction.None, ps.main.stopAction,
                "loops=true (continuous) must configure stopAction=None.");
        }
    }
}
