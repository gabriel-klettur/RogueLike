using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Game.VFX
{
    /// <summary>
    /// Covers composite particle presets: a <see cref="ParticlePresetDefinition.layers"/>
    /// list, each valid entry rendered by the same emitter as its own child
    /// ParticleSystem alongside the root — additive light over alpha mass, fast sparks
    /// over slow haze, without hand-placing a separate preset per role. See the
    /// vfx-authoring skill §1 "Layering" and <c>ParticleEmitter.Layers.cs</c>.
    /// </summary>
    [TestFixture]
    public class ParticleEmitterLayersTests
    {
        private readonly List<GameObject> _createdGos = new List<GameObject>();
        private readonly List<ScriptableObject> _createdSos = new List<ScriptableObject>();

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGos)
                if (go != null) Object.DestroyImmediate(go);
            _createdGos.Clear();

            foreach (var so in _createdSos)
                if (so != null) Object.DestroyImmediate(so);
            _createdSos.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ── Fixtures ─────────────────────────────────────────────────────────────

        private ParticleEmitter CreateEmitter(string name = "LayersTestEmitter")
        {
            var go = new GameObject(name);
            _createdGos.Add(go);
            return go.AddComponent<ParticleEmitter>();
        }

        private ParticlePresetDefinition MakePreset(string id, string kind, bool loops, bool additive = false)
        {
            var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _createdSos.Add(def);
            def.id = id;
            def.displayName = id;
            def.type = kind;
            def.vfx = new ParticleVfxParams
            {
                kind      = kind,
                loops     = loops,
                additive  = additive,
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

        /// <summary>The root ParticleSystem lives on a child named "Particles" (see EnsureParticleSystem).</summary>
        private static ParticleSystem GetRootPs(ParticleEmitter emitter)
            => emitter.transform.Find("Particles")?.GetComponent<ParticleSystem>();

        // ── Composite build ──────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_TwoValidLayers_BuildsExactlyTwoLayerSystemsPlusRoot()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_root", "aura", true);
            root.layers.Add(MakePreset("layers_a", "aura", true));
            root.layers.Add(MakePreset("layers_b", "explosion", false));

            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(2, emitter.LayerSystems.Count, "Two valid layer entries must build two child systems.");
            Assert.IsNotNull(GetRootPs(emitter), "The root system must still exist alongside its layers.");
            for (int i = 0; i < emitter.LayerSystems.Count; i++)
                Assert.IsNotNull(emitter.LayerSystems[i], $"Layer slot {i} must not be null.");
        }

        [Test]
        public void ApplyPreset_LayerScaleMultiplier_PropagatesToLayerStartSize()
        {
            var emitter = CreateEmitter();
            var layer = MakePreset("layers_scaled_child", "aura", true);
            layer.vfx.sizeMin = 0.2f;
            layer.vfx.sizeMax = 0.2f;

            var root = MakePreset("layers_scaled_root", "aura", true);
            root.layers.Add(layer);

            emitter.ApplyPreset(root, 4f);

            var layerPs = emitter.LayerSystems[0];
            Assert.AreEqual(0.8f, layerPs.main.startSize.constantMax, 0.001f,
                "Every layer must be scaled by the emitter's scaleMultiplier exactly like the root vfx.");
        }

        // ── Skip rules ───────────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_NullLayerEntry_IsSkipped()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_null_root", "aura", true);
            root.layers.Add(null);
            root.layers.Add(MakePreset("layers_null_valid", "aura", true));

            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count, "A null layer entry must not build a child system.");
        }

        [Test]
        public void ApplyPreset_SelfReferencingLayer_IsSkipped()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_self_root", "aura", true);
            root.layers.Add(root);
            root.layers.Add(MakePreset("layers_self_valid", "aura", true));

            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count,
                "A layer referencing its own preset would recurse forever if not skipped.");
        }

        [Test]
        public void ApplyPreset_LightningKindLayer_IsSkipped()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_lightning_root", "aura", true);
            root.layers.Add(MakePreset("layers_lightning_child", "lightning", true));
            root.layers.Add(MakePreset("layers_lightning_valid", "aura", true));

            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count,
                "Lightning draws with a LineRenderer, not a ParticleSystem — it cannot be a layer.");
        }

        [Test]
        public void ApplyPreset_NestedLayers_AreNotRecursed()
        {
            var emitter = CreateEmitter();
            var grandchild = MakePreset("layers_grandchild", "aura", true);
            var child = MakePreset("layers_child", "aura", true);
            child.layers.Add(grandchild);

            var root = MakePreset("layers_nested_root", "aura", true);
            root.layers.Add(child);

            emitter.ApplyPreset(root, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count,
                "Only one level of layers is honoured — a layer's OWN layers list must be ignored.");
        }

        // ── Shrinking the stack ──────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_ReapplyWithFewerLayers_DestroysTheExtraChildren()
        {
            var emitter = CreateEmitter();
            var rootMany = MakePreset("layers_shrink_root_many", "aura", true);
            rootMany.layers.Add(MakePreset("layers_shrink_a", "aura", true));
            rootMany.layers.Add(MakePreset("layers_shrink_b", "aura", true));
            emitter.ApplyPreset(rootMany, 1f);
            Assert.IsNotNull(emitter.transform.Find("Layer_1"), "Sanity: two layers built two children.");

            var rootFew = MakePreset("layers_shrink_root_few", "aura", true);
            rootFew.layers.Add(MakePreset("layers_shrink_c", "aura", true));
            emitter.ApplyPreset(rootFew, 1f);

            Assert.AreEqual(1, emitter.LayerSystems.Count);
            Assert.IsNull(emitter.transform.Find("Layer_1"),
                "A preset with fewer layers must destroy the leftover child, not just stop tracking it.");
        }

        // ── Materials ────────────────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_AdditiveLayerOverAlphaRoot_GetDifferentSharedMaterials()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_material_root", "aura", true, additive: false);
            root.layers.Add(MakePreset("layers_material_layer", "aura", true, additive: true));

            emitter.ApplyPreset(root, 1f);

            var rootRenderer = GetRootPs(emitter).GetComponent<ParticleSystemRenderer>();
            var layerRenderer = emitter.LayerSystems[0].GetComponent<ParticleSystemRenderer>();

            Assert.AreNotSame(rootRenderer.sharedMaterial, layerRenderer.sharedMaterial,
                "Additive and alpha blending must resolve to different cached materials.");
        }

        // ── Emission control ─────────────────────────────────────────────────────

        [Test]
        public void StopEmitting_StopsEveryLayerAlongsideTheRoot()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_stop_root", "aura", true);
            root.layers.Add(MakePreset("layers_stop_a", "aura", true));
            root.layers.Add(MakePreset("layers_stop_b", "aura", true));
            emitter.ApplyPreset(root, 1f);

            emitter.StopEmitting();

            for (int i = 0; i < emitter.LayerSystems.Count; i++)
                Assert.IsFalse(emitter.LayerSystems[i].isEmitting, $"Layer {i} must stop emitting too.");
        }

        // ── Lightning teardown ───────────────────────────────────────────────────

        [Test]
        public void ApplyPreset_LightningAfterComposite_LeavesZeroLayerSystems()
        {
            var emitter = CreateEmitter();
            var root = MakePreset("layers_teardown_root", "aura", true);
            root.layers.Add(MakePreset("layers_teardown_a", "aura", true));
            root.layers.Add(MakePreset("layers_teardown_b", "aura", true));
            emitter.ApplyPreset(root, 1f);
            Assert.AreEqual(2, emitter.LayerSystems.Count, "Sanity: composite built two layers.");

            emitter.ApplyPreset(MakePreset("layers_teardown_lightning", "lightning", true), 1f);

            Assert.AreEqual(0, emitter.LayerSystems.Count,
                "Switching to a lightning root must tear down every layer left over from the previous composite.");
        }
    }
}
