using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers re-applying different presets to the SAME <see cref="ParticleEmitter"/>.
    ///
    /// This is not an exotic path: the Particles Editor's View panel keeps one emitter and
    /// calls ApplyPreset on it for every preset the user clicks, so anything the previous
    /// preset leaves behind shows up on top of the next one. Two defects lived here:
    ///
    ///  • A finished burst (loops = false) sets stopAction = Disable, which deactivates the
    ///    child holding the ParticleSystem. Play() on an inactive GameObject is silently
    ///    ignored, so the emitter stayed dead for every preset selected afterwards.
    ///  • The "lightning" kind runs a while(true) coroutine driving a LineRenderer, and
    ///    nothing stopped it when a non-lightning preset was applied, so the bolt kept
    ///    drawing over everything chosen later.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterPresetSwitchTests
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

        // ── Fixtures ─────────────────────────────────────────────────────────────

        private ParticleEmitter CreateEmitter(string name = "PresetSwitchTestEmitter")
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        private static ParticlePresetDefinition MakePreset(string id, string kind, bool loops)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            def.id = id;
            def.displayName = id;
            def.type = kind;
            def.vfx = new ParticleVfxParams
            {
                kind      = kind,
                loops     = loops,
                emitRate  = 20f,
                count     = 8,
                lifespan  = 0.25f,
                speed     = 1f,
                sizeMin   = 0.1f,
                sizeMax   = 0.3f,
                segments  = 8,
                thickness = 0.1f,
            };
            return def;
        }

        private static ParticlePresetDefinition LoopingAura() => MakePreset("switch_loop_aura", "aura", true);
        private static ParticlePresetDefinition BurstExplosion() => MakePreset("switch_burst_explosion", "explosion", false);
        private static ParticlePresetDefinition Lightning() => MakePreset("switch_lightning", "lightning", true);

        /// <summary>Finds the ParticleSystem even when its GameObject has been deactivated.</summary>
        private static ParticleSystem GetPs(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<ParticleSystem>(true);

        private static LineRenderer GetLr(ParticleEmitter emitter)
            => emitter.GetComponentInChildren<LineRenderer>(true);

        // ── Burst revival ────────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_AfterBurstDisabledItsChild_ReactivatesTheParticleSystem()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(BurstExplosion(), 1f);

            var ps = GetPs(emitter);
            Assert.IsNotNull(ps, "Sanity: a burst preset must still build a ParticleSystem.");

            // Reproduce what stopAction = Disable does once the burst finishes.
            ps.gameObject.SetActive(false);

            emitter.ApplyPreset(LoopingAura(), 1f);

            Assert.IsTrue(ps.gameObject.activeSelf,
                "Applying a new preset must wake a child that a previous burst deactivated — " +
                "Play() is a no-op while it is inactive, which left the emitter dead forever.");
        }

        [Test]
        public void ApplyPreset_AfterBurstDisabledItsChild_DoesNotBuildASecondParticleSystem()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(BurstExplosion(), 1f);
            GetPs(emitter).gameObject.SetActive(false);

            emitter.ApplyPreset(LoopingAura(), 1f);

            var all = emitter.GetComponentsInChildren<ParticleSystem>(true);
            Assert.AreEqual(1, all.Length,
                "The inactive child must be found and reused, not skipped and duplicated.");
        }

        [Test]
        public void ApplyPreset_AfterBurstDisabledItsChild_EmitsAgain()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(BurstExplosion(), 1f);
            var ps = GetPs(emitter);
            ps.gameObject.SetActive(false);

            emitter.ApplyPreset(LoopingAura(), 1f);

            ps.Simulate(0.5f, withChildren: true, restart: false);
            Assert.Greater(ps.particleCount, 0,
                "A revived emitter must actually emit for the newly selected preset.");
        }

        // ── Lightning teardown ───────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_LightningThenParticles_DisablesTheLineRenderer()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Lightning(), 1f);

            var lr = GetLr(emitter);
            Assert.IsNotNull(lr, "Sanity: the lightning kind must build a LineRenderer.");
            Assert.IsTrue(lr.enabled, "Sanity: the bolt is visible while the lightning preset is applied.");

            emitter.ApplyPreset(LoopingAura(), 1f);

            Assert.IsFalse(lr.enabled,
                "Switching away from lightning must hide the bolt — its animation coroutine " +
                "never ends on its own and would keep drawing over every later preset.");
        }

        [Test]
        public void ApplyPreset_LightningThenParticles_BuildsAWorkingParticleSystem()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Lightning(), 1f);
            emitter.ApplyPreset(LoopingAura(), 1f);

            var ps = GetPs(emitter);
            Assert.IsNotNull(ps, "A particle preset applied after lightning must have a ParticleSystem.");
            Assert.IsTrue(ps.gameObject.activeSelf,
                "The particle child parked while lightning was active must be woken up again.");

            ps.Simulate(0.5f, withChildren: true, restart: false);
            Assert.Greater(ps.particleCount, 0);
        }

        [Test]
        public void ApplyPreset_ParticlesThenLightning_ParksTheParticleSystemInsteadOfDestroyingIt()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(LoopingAura(), 1f);
            var ps = GetPs(emitter);
            Assert.IsNotNull(ps);

            emitter.ApplyPreset(Lightning(), 1f);

            Assert.IsTrue(ps != null,
                "The particle child must be parked, not destroyed — a deferred Destroy lets a " +
                "quick switch back build a second system beside the dying one.");
            Assert.IsFalse(ps.gameObject.activeSelf,
                "Lightning draws with a LineRenderer, so the particle child must be inactive.");
        }

        [Test]
        public void ApplyPreset_LightningTwice_ReusesTheSameLineRenderer()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Lightning(), 1f);
            var first = GetLr(emitter);

            emitter.ApplyPreset(LoopingAura(), 1f);
            emitter.ApplyPreset(Lightning(), 1f);

            var all = emitter.GetComponentsInChildren<LineRenderer>(true);
            Assert.AreEqual(1, all.Length, "Round-tripping through lightning must not leak LineRenderers.");
            Assert.AreSame(first, all[0]);
            Assert.IsTrue(all[0].enabled, "Re-selecting lightning must make the bolt visible again.");
        }

        [Test]
        public void ApplyPreset_LightningTwice_DoesNotAllocateANewMaterialEachTime()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(Lightning(), 1f);
            var mat = GetLr(emitter).sharedMaterial;
            Assert.IsNotNull(mat, "Sanity: the bolt needs a material.");

            emitter.ApplyPreset(LoopingAura(), 1f);
            emitter.ApplyPreset(Lightning(), 1f);

            Assert.AreSame(mat, GetLr(emitter).sharedMaterial,
                "Re-applying lightning must reuse the material instead of leaking one per switch.");
        }

        // ── Module leaks across presets ──────────────────────────────────────────
        //
        // ConfigureParticleSystem/ConfigureShape only write the properties the incoming
        // preset cares about. On a reused emitter every property they skip keeps the
        // PREVIOUS preset's value, which deforms the new effect. A freshly spawned map
        // emitter never sees this; the editor's single preview emitter sees it constantly.

        private static ParticlePresetDefinition WithDrag(float drag)
        {
            var def = MakePreset("switch_drag", "aura", true);
            def.vfx.drag = drag;
            def.vfx.speed = 5f;
            return def;
        }

        [Test]
        public void ApplyPreset_DragPresetThenDraglessPreset_DisablesVelocityLimiting()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(WithDrag(0.9f), 1f);
            var ps = GetPs(emitter);
            Assert.IsTrue(ps.limitVelocityOverLifetime.enabled, "Sanity: drag > 0 enables the module.");

            emitter.ApplyPreset(WithDrag(0f), 1f);

            Assert.IsFalse(ps.limitVelocityOverLifetime.enabled,
                "A preset without drag must switch the limiter off — leaving it on clamped every " +
                "later effect to the previous preset's speed.");
        }

        [Test]
        public void ApplyPreset_ConeKindThenCircleKind_ResetsShapeRotation()
        {
            var emitter = CreateEmitter();
            // "slash" builds a Cone rotated -90 on X so it faces forward.
            emitter.ApplyPreset(MakePreset("switch_slash", "slash", true), 1f);
            var ps = GetPs(emitter);
            Assert.AreNotEqual(Vector3.zero, ps.shape.rotation, "Sanity: the cone is rotated.");

            emitter.ApplyPreset(MakePreset("switch_aura", "aura", true), 1f);

            Assert.AreEqual(Vector3.zero, ps.shape.rotation,
                "A Circle shape never writes rotation, so an inherited -90 would emit the ring " +
                "flat on its side instead of facing the camera.");
            Assert.AreEqual(ParticleSystemShapeType.Circle, ps.shape.shapeType);
        }

        [Test]
        public void ApplyPreset_BoxKindThenCircleKind_ResetsShapeScale()
        {
            var emitter = CreateEmitter();
            // "water_flow" builds a Box scaled 3 x 0.1 x 0.1.
            emitter.ApplyPreset(MakePreset("switch_flow", "water_flow", true), 1f);
            var ps = GetPs(emitter);
            Assert.AreNotEqual(Vector3.one, ps.shape.scale, "Sanity: the box is scaled.");

            emitter.ApplyPreset(MakePreset("switch_aura2", "aura", true), 1f);

            Assert.AreEqual(Vector3.one, ps.shape.scale,
                "An inherited box scale squashes the ring into a thin horizontal ellipse.");
        }

        [Test]
        public void ApplyPreset_EdgeEmittingKindThenVolumeKind_ResetsRadiusThickness()
        {
            var emitter = CreateEmitter();
            // "aura" emits from the edge only (radiusThickness 0).
            emitter.ApplyPreset(MakePreset("switch_aura3", "aura", true), 1f);
            var ps = GetPs(emitter);
            Assert.AreEqual(0f, ps.shape.radiusThickness, 1e-4f, "Sanity: an aura emits edge-only.");

            // "explosion" never writes radiusThickness, so it used to inherit the 0.
            emitter.ApplyPreset(MakePreset("switch_boom", "explosion", false), 1f);

            Assert.AreEqual(1f, ps.shape.radiusThickness, 1e-4f,
                "An explosion must emit from the whole sphere volume, not from a shell it " +
                "inherited from whichever preset happened to be selected before it.");
        }

        [Test]
        public void ApplyPreset_BurstThenLooping_ClearsTheBurstList()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(BurstExplosion(), 1f);
            var ps = GetPs(emitter);
            Assert.AreEqual(1, ps.emission.burstCount, "Sanity: a burst preset registers one Burst.");

            emitter.ApplyPreset(LoopingAura(), 1f);

            Assert.AreEqual(0, ps.emission.burstCount,
                "The burst list is independent of rateOverTime — an uncleared burst kept firing " +
                "the old explosion on top of the new continuous emitter at every loop boundary.");
        }

        [Test]
        public void ApplyPreset_LoopingThenBurst_StillRegistersTheBurst()
        {
            var emitter = CreateEmitter();
            emitter.ApplyPreset(LoopingAura(), 1f);
            emitter.ApplyPreset(BurstExplosion(), 1f);

            var ps = GetPs(emitter);
            Assert.AreEqual(1, ps.emission.burstCount,
                "Clearing bursts on the continuous path must not stop the burst path setting one.");
            Assert.AreEqual(0f, ps.emission.rateOverTime.constant, 1e-4f,
                "A burst preset emits only through its Burst, never continuously.");
        }

        [Test]
        public void ApplyPreset_SpinningPresetThenStillPreset_DisablesRotation()
        {
            var emitter = CreateEmitter();

            var spinning = MakePreset("switch_spin", "aura", true);
            spinning.vfx.rotationSpeedDegrees = 120f;
            emitter.ApplyPreset(spinning, 1f);
            var ps = GetPs(emitter);
            Assert.IsTrue(ps.rotationOverLifetime.enabled, "Sanity: a spin speed enables the module.");

            emitter.ApplyPreset(MakePreset("switch_still", "aura", true), 1f);

            Assert.IsFalse(ps.rotationOverLifetime.enabled,
                "A preset that asks for no spin must switch the module off — inherited rotation " +
                "makes a still effect drift for no reason the author can see.");
        }

        [Test]
        public void ApplyPreset_RotationJitter_SpreadsStartRotationBothWays()
        {
            var emitter = CreateEmitter();
            var def = MakePreset("switch_jitter", "aura", true);
            def.vfx.startRotationJitterDegrees = 180f;

            emitter.ApplyPreset(def, 1f);

            var start = GetPs(emitter).main.startRotation;
            Assert.Less(start.constantMin, 0f,
                "Jitter must be symmetric: a one-sided range biases every particle the same way, " +
                "which is visible as a pattern rather than as randomness.");
            Assert.Greater(start.constantMax, 0f);
        }

        [Test]
        public void ApplyPreset_NoJitter_LeavesStartRotationAtZero()
        {
            var emitter = CreateEmitter();
            var spun = MakePreset("switch_spun", "aura", true);
            spun.vfx.startRotationJitterDegrees = 180f;
            emitter.ApplyPreset(spun, 1f);

            emitter.ApplyPreset(MakePreset("switch_flat", "aura", true), 1f);

            var start = GetPs(emitter).main.startRotation;
            Assert.AreEqual(0f, start.constantMax, 1e-4f,
                "Same leak family as the shape and drag modules: written unconditionally so a " +
                "reused emitter cannot inherit the previous preset's jitter.");
        }

        // ── Repeated switching ───────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_CyclingThroughEveryKind_LeavesExactlyOneOfEachRenderer()
        {
            var emitter = CreateEmitter();

            for (int i = 0; i < 3; i++)
            {
                emitter.ApplyPreset(LoopingAura(), 1f);
                emitter.ApplyPreset(BurstExplosion(), 1f);
                emitter.ApplyPreset(Lightning(), 1f);
            }
            emitter.ApplyPreset(LoopingAura(), 1f);

            Assert.AreEqual(1, emitter.GetComponentsInChildren<ParticleSystem>(true).Length);
            Assert.AreEqual(1, emitter.GetComponentsInChildren<LineRenderer>(true).Length);
            Assert.IsFalse(GetLr(emitter).enabled, "The final preset is not lightning, so the bolt stays hidden.");
            Assert.IsTrue(GetPs(emitter).gameObject.activeSelf, "The final preset is a particle kind, so its child is live.");
        }
    }
}
