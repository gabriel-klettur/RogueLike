using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Covers per-instance size overrides: the ratios a resized placement carries, how
    /// <see cref="ParticleOverrideApplier"/> folds them over the shared preset, and their
    /// round trip through the world file.
    ///
    /// The reason they exist at all is that particle parameters live on the PRESET. Resizing
    /// one pollen field by editing <c>spawnWidth</c> would resize all 58 of them, so the
    /// instance carries multipliers instead and the emitter applies them as it builds. Two
    /// invariants make that safe, and both are asserted here: the preset object is never
    /// mutated, and an instance that has never been resized shares the preset's own block
    /// rather than a copy of it.
    /// </summary>
    [TestFixture]
    public class ParticleInstanceOverridesTests
    {
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp() => LogAssert.ignoreFailingMessages = true;

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            LogAssert.ignoreFailingMessages = false;
        }

        private static ParticleVfxParams BoxBlock()
            => new ParticleVfxParams
            {
                kind = "aura",
                loops = true,
                spawnWidth = 2f,
                spawnHeight = 1f,
                radius = 0.5f,
                speed = 1f,
                lifespan = 2f,
                gravity = 3f,
                gravityVector = new Vector2(0.2f, -0.4f),
                useGravityVector = true,
                radialSpeed = -0.5f,
                noiseEnabled = true,
                noiseStrength = 0.3f,
                swayAmp = 0.1f,
            };

        // ── The struct ───────────────────────────────────────────────────────────

        [Test]
        public void DefaultConstructed_IsSanitisedToInherit()
        {
            // `default(ParticleInstanceOverrides)` is all zeros, which read literally means
            // "shrink this emitter to nothing" — and that is what a serializer, an array
            // resize or a missing JSON key hands you.
            var zeroed = default(ParticleInstanceOverrides).Sanitized();

            Assert.IsTrue(zeroed.IsDefault);
            Assert.AreEqual(1f, zeroed.spawnScaleX, 1e-5f);
        }

        [Test]
        public void Sanitized_RejectsNaNAndClampsToTheAuthoringRange()
        {
            var wild = new ParticleInstanceOverrides(float.NaN, 500f, -3f).Sanitized();

            Assert.AreEqual(1f, wild.spawnScaleX, 1e-5f, "A NaN ratio propagates into every " +
                "size the emitter writes and takes the whole system down silently.");
            Assert.AreEqual(ParticleInstanceOverrides.MaxRatio, wild.spawnScaleY, 1e-5f);
            Assert.AreEqual(1f, wild.reachScale, 1e-5f);
        }

        // ── The applier ──────────────────────────────────────────────────────────

        [Test]
        public void Apply_WithDefaults_ReturnsTheVerySameBlock()
        {
            var block = BoxBlock();

            Assert.AreSame(block, ParticleOverrideApplier.Apply(block, ParticleInstanceOverrides.None),
                "An instance nobody resized must keep sharing the preset's data — cloning 185 " +
                "blocks on load to change nothing is pure waste.");
        }

        [Test]
        public void Apply_NeverMutatesThePreset()
        {
            var block = BoxBlock();

            ParticleOverrideApplier.Apply(block, new ParticleInstanceOverrides(3f, 3f, 3f));

            Assert.AreEqual(2f, block.spawnWidth, 1e-5f,
                "The source block belongs to a ScriptableObject every placement shares; " +
                "writing to it resizes all of them.");
            Assert.AreEqual(1f, block.speed, 1e-5f);
        }

        [Test]
        public void Apply_ScalesAnAuthoredSpawnBoxPerAxis()
        {
            var result = ParticleOverrideApplier.Apply(BoxBlock(), new ParticleInstanceOverrides(2f, 0.5f, 1f));

            Assert.AreEqual(4f, result.spawnWidth, 1e-4f);
            Assert.AreEqual(0.5f, result.spawnHeight, 1e-4f);
        }

        [Test]
        public void Apply_ScalesACircleByTheGeometricMean()
        {
            var circle = BoxBlock();
            circle.spawnWidth = 0f;
            circle.spawnHeight = 0f;
            circle.radius = 2f;

            var result = ParticleOverrideApplier.Apply(circle, new ParticleInstanceOverrides(4f, 1f, 1f));

            Assert.AreEqual(4f, result.radius, 1e-3f,
                "A circle has one radius and the emitter has no ellipse to give it, so the two " +
                "axis ratios fold into their geometric mean — sqrt(4 x 1) = 2, on a radius of 2.");
        }

        [Test]
        public void Apply_MaterialisesTheStripKindsBoxSoItHasSomethingToScale()
        {
            var leaf = BoxBlock();
            leaf.kind = "falling_leaf";
            leaf.spawnWidth = 0f;
            leaf.spawnHeight = 0f;

            var result = ParticleOverrideApplier.Apply(leaf, new ParticleInstanceOverrides(2f, 1f, 1f));

            Assert.AreEqual(4f, result.spawnWidth, 1e-4f,
                "falling_leaf's 2-unit strip is hard-coded in the emitter; the override writes " +
                "it down first so there is a field to multiply.");
            Assert.AreEqual(0.1f, result.spawnHeight, 1e-4f);
        }

        [Test]
        public void Apply_ReachScalesEveryMotionTerm_AndLeavesLifespanAlone()
        {
            var result = ParticleOverrideApplier.Apply(BoxBlock(), new ParticleInstanceOverrides(1f, 1f, 2f));

            Assert.AreEqual(2f, result.speed, 1e-4f);
            Assert.AreEqual(6f, result.gravity, 1e-4f);
            Assert.AreEqual(-0.8f, result.gravityVector.y, 1e-4f);
            Assert.AreEqual(-1f, result.radialSpeed, 1e-4f);
            Assert.AreEqual(0.6f, result.noiseStrength, 1e-4f);
            Assert.AreEqual(0.2f, result.swayAmp, 1e-4f);

            Assert.AreEqual(2f, result.lifespan, 1e-4f,
                "Stretching lifespan would change how many particles are alive at once — the " +
                "density and the frame cost — which is a different edit from 'reaches further'.");
        }

        // ── The emitter and the marker agree ─────────────────────────────────────

        [Test]
        public void ResizedInstance_MarkerAndEffectStayInStep()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "override_probe";
            def.displayName = def.id;
            def.vfx = BoxBlock();
            def.layers = new List<ParticlePresetDefinition>();

            var go = new GameObject("OverrideProbe");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();

            emitter.ApplyPreset(def, 1f, new ParticleInstanceOverrides(2f, 2f, 1f));

            var ps = go.GetComponentInChildren<ParticleSystem>(true);
            Assert.AreEqual(4f, ps.shape.scale.x, 1e-3f,
                "The emitter builds its shape from the OVERRIDDEN block, or the resize does " +
                "nothing to the particles.");

            var marker = ParticleFootprint.OfEmission(def, 1f, emitter.Overrides);
            Assert.AreEqual(2f, marker.HalfWidth, 1e-3f,
                "And the marker reads the same override through the same applier, which is " +
                "what keeps the box on the effect.");
        }

        [Test]
        public void SetOverrides_RebuildsTheEmitterAtTheNewSize()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "override_rebuild";
            def.displayName = def.id;
            def.vfx = BoxBlock();
            def.layers = new List<ParticlePresetDefinition>();

            var go = new GameObject("OverrideRebuild");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(def, 1f);

            emitter.SetOverrides(new ParticleInstanceOverrides(3f, 1f, 1f));

            var ps = go.GetComponentInChildren<ParticleSystem>(true);
            Assert.AreEqual(6f, ps.shape.scale.x, 1e-3f,
                "A drag pushes a new size every frame; it has to reach the live systems.");
        }

        [Test]
        public void ReApplyingThePreset_KeepsTheInstancesOwnSize()
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _created.Add(def);
            def.id = "override_reapply";
            def.displayName = def.id;
            def.vfx = BoxBlock();
            def.layers = new List<ParticlePresetDefinition>();

            var go = new GameObject("OverrideReapply");
            _created.Add(go);
            var emitter = go.AddComponent<ParticleEmitter>();
            emitter.ApplyPreset(def, 1f, new ParticleInstanceOverrides(2f, 1f, 1f));

            // The F1 editor re-applies the preset to every live emitter on each property edit,
            // and the culling loader re-applies on re-enable. Neither knows about this
            // instance's size, and both must leave it alone.
            emitter.ApplyPreset(def, 1f);

            Assert.AreEqual(2f, emitter.Overrides.spawnScaleX, 1e-4f);
        }

        // ── Persistence ──────────────────────────────────────────────────────────

        [Test]
        public void Serializer_WritesOverridesOnlyWhenTheyDifferFromThePreset()
        {
            var plain = MakeInstance("plain", ParticleInstanceOverrides.None);
            string json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { plain }, null);

            Assert.IsFalse(json.Contains("spawn_scale_x"),
                "185 records with three redundant keys each is noise in every diff of a file " +
                "that is reviewed by reading it.");

            var resized = MakeInstance("resized", new ParticleInstanceOverrides(1.5f, 0.5f, 2f));
            string resizedJson = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { resized }, null);

            Assert.IsTrue(resizedJson.Contains("\"spawn_scale_x\":1.5000"));
            Assert.IsTrue(resizedJson.Contains("\"reach\":2.0000"));
        }

        [Test]
        public void Serializer_RoundTripsThem()
        {
            var resized = MakeInstance("round", new ParticleInstanceOverrides(1.25f, 0.75f, 3f));
            string json = ParticleInstanceSerializer.Serialize(
                new List<PersistedParticleInstance> { resized }, null);

            var records = ParticleInstanceSerializer.Deserialize(json, null);

            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(1.25f, records[0].Overrides.spawnScaleX, 1e-3f);
            Assert.AreEqual(0.75f, records[0].Overrides.spawnScaleY, 1e-3f);
            Assert.AreEqual(3f, records[0].Overrides.reachScale, 1e-3f);
        }

        [Test]
        public void Serializer_ReadsAPreV3RecordAsInheritingThePreset()
        {
            string v2 = "{\"version\":2,\"instances\":[" +
                        "{\"id\":\"abc\",\"preset_id\":\"leaf\",\"zone\":\"\",\"rel_x\":0,\"rel_y\":0," +
                        "\"scale_multiplier\":1.0}]}";

            var records = ParticleInstanceSerializer.Deserialize(v2, null);

            Assert.AreEqual(1, records.Count);
            Assert.IsTrue(records[0].Overrides.IsDefault,
                "Every world file written before this feature has to keep meaning exactly what " +
                "it meant.");
        }

        private PersistedParticleInstance MakeInstance(string name, ParticleInstanceOverrides overrides)
        {
            var go = new GameObject("PE_" + name);
            _created.Add(go);
            var inst = go.AddComponent<PersistedParticleInstance>();
            inst.Restore("some_preset", System.Guid.NewGuid().ToString("N"), 1f, overrides);
            return inst;
        }
    }
}
