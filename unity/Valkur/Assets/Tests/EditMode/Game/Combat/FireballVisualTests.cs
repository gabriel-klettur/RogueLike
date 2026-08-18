using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Game.Combat
{
    /// <summary>
    /// Robustness tests for the procedural fireball visual rig:
    ///   * shared sprite/material caching
    ///   * child layer construction (halo / glow / core / hot core / ghosts)
    ///   * impact gating (OnImpact is idempotent)
    ///   * pool-safe cleanup of dynamic Light2D on disable
    /// EditMode-only: avoids physics/Awake side effects via direct AddComponent.
    /// </summary>
    public class FireballVisualTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            // Suppress sprite/material init warnings emitted by the procedural texture pipeline.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;

            // Destroy any FireballImpactFX / BurstEmber / Ember objects spawned by tests
            // so they don't leak into other tests.
            foreach (var stray in Object.FindObjectsOfType<FireballImpactFX>())
                Object.DestroyImmediate(stray.gameObject);
            foreach (var ember in Object.FindObjectsOfType<FireballEmber>())
                Object.DestroyImmediate(ember.gameObject);
        }

        private FireballVisual CreateVisual()
        {
            _go = new GameObject("Fireball");
            // Match the placeholder root SpriteRenderer added by ProjectilePrefabFactory.
            _go.AddComponent<SpriteRenderer>();
            var fb = _go.AddComponent<FireballVisual>();
            // EditMode doesn't fire Awake/OnEnable on AddComponent, so invoke them manually.
            InvokePrivate(fb, "Awake");
            InvokePrivate(fb, "OnEnable");
            return fb;
        }

        private static void InvokePrivate(object instance, string name)
        {
            var m = instance.GetType().GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (m != null) m.Invoke(instance, null);
        }

        // ── Shared assets ────────────────────────────────────────────

        [Test]
        public void SharedAssets_AreNonNull_AfterFirstAccess()
        {
            Assert.IsNotNull(FireballVisual.SharedUnlitMaterial);
            Assert.IsNotNull(FireballVisual.SharedGlowSprite);
            Assert.IsNotNull(FireballVisual.SharedRingSprite);
            Assert.IsNotNull(FireballVisual.SharedEmberSprite);
            Assert.IsNotNull(FireballVisual.SharedHotCoreSprite);
        }

        [Test]
        public void SharedAssets_AreCached_AcrossCalls()
        {
            var mat1   = FireballVisual.SharedUnlitMaterial;
            var glow1  = FireballVisual.SharedGlowSprite;
            var ring1  = FireballVisual.SharedRingSprite;
            var ember1 = FireballVisual.SharedEmberSprite;

            var mat2   = FireballVisual.SharedUnlitMaterial;
            var glow2  = FireballVisual.SharedGlowSprite;
            var ring2  = FireballVisual.SharedRingSprite;
            var ember2 = FireballVisual.SharedEmberSprite;

            Assert.AreSame(mat1, mat2,   "Material should be cached across calls");
            Assert.AreSame(glow1, glow2, "Glow sprite should be cached across calls");
            Assert.AreSame(ring1, ring2, "Ring sprite should be cached across calls");
            Assert.AreSame(ember1, ember2, "Ember sprite should be cached across calls");
        }

        [Test]
        public void RingSprite_IsAnnular_HighAlphaAtBand_LowAtCenter()
        {
            var tex = FireballVisual.SharedRingSprite.texture;
            int size = tex.width;
            float c = size * 0.5f;

            // Centre pixel — should be transparent (ring has hollow centre).
            Color centre = tex.GetPixel(Mathf.RoundToInt(c), Mathf.RoundToInt(c));
            // Pixel near the ring band (~0.78 * radius).
            int band = Mathf.RoundToInt(c + c * 0.78f * 0.95f);
            Color onBand = tex.GetPixel(band, Mathf.RoundToInt(c));

            Assert.Less(centre.a, 0.2f, "Ring centre should be near-transparent");
            Assert.Greater(onBand.a, 0.4f, "Ring band should be opaque");
        }

        // ── Visual rig construction ──────────────────────────────────

        [Test]
        public void Awake_BuildsAllLayerChildren()
        {
            var fb = CreateVisual();
            string[] expected = { "Halo", "Glow", "Core", "HotCore" };
            foreach (var name in expected)
            {
                var child = fb.transform.Find(name);
                Assert.IsNotNull(child, $"Expected child '{name}' was not built");
                Assert.IsNotNull(child.GetComponent<SpriteRenderer>(),
                    $"Child '{name}' must have a SpriteRenderer");
            }
        }

        [Test]
        public void Awake_BuildsExactlyFiveGhostTrailRenderers()
        {
            var fb = CreateVisual();
            int ghostCount = 0;
            for (int i = 0; i < fb.transform.childCount; i++)
            {
                if (fb.transform.GetChild(i).name.StartsWith("Ghost"))
                    ghostCount++;
            }
            Assert.AreEqual(5, ghostCount, "Expected exactly 5 ghost trail renderers (GhostCount=5)");
        }

        [Test]
        public void Awake_DisablesPlaceholderRootSpriteRenderer()
        {
            var fb = CreateVisual();
            var rootSr = fb.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(rootSr);
            Assert.IsFalse(rootSr.enabled,
                "Root placeholder SpriteRenderer must be disabled (avoids double-rendering with procedural layers)");
        }

        [Test]
        public void LayerRenderers_UseEntitiesSortingLayerAndUnlitMaterial()
        {
            var fb = CreateVisual();
            string[] names = { "Halo", "Glow", "Core", "HotCore" };
            foreach (var name in names)
            {
                var sr = fb.transform.Find(name).GetComponent<SpriteRenderer>();
                Assert.AreEqual(SortingConfig.LAYER_ENTITIES, sr.sortingLayerName,
                    $"{name}.sortingLayerName must be Entities");
                Assert.AreSame(FireballVisual.SharedUnlitMaterial, sr.sharedMaterial,
                    $"{name} should reuse the shared unlit material (no per-instance allocations)");
            }
        }

        [Test]
        public void LayerRenderers_HaveAscendingSortingOrder_HotCoreOnTop()
        {
            var fb = CreateVisual();
            int halo = fb.transform.Find("Halo").GetComponent<SpriteRenderer>().sortingOrder;
            int glow = fb.transform.Find("Glow").GetComponent<SpriteRenderer>().sortingOrder;
            int core = fb.transform.Find("Core").GetComponent<SpriteRenderer>().sortingOrder;
            int hot  = fb.transform.Find("HotCore").GetComponent<SpriteRenderer>().sortingOrder;

            Assert.Less(halo, glow, "Halo should render below Glow");
            Assert.Less(glow, core, "Glow should render below Core");
            Assert.Less(core, hot,  "Core should render below HotCore");
        }

        // ── Impact gating ────────────────────────────────────────────

        [Test]
        public void OnImpact_FirstCall_SpawnsImpactFx()
        {
            var fb = CreateVisual();
            int before = Object.FindObjectsOfType<FireballImpactFX>().Length;
            fb.OnImpact(Vector3.zero);
            int after = Object.FindObjectsOfType<FireballImpactFX>().Length;
            Assert.AreEqual(before + 1, after, "OnImpact should spawn exactly one FireballImpactFX");
        }

        [Test]
        public void OnImpact_SecondCallOnSameFireball_IsIgnored()
        {
            var fb = CreateVisual();
            fb.OnImpact(Vector3.zero);
            int afterFirst = Object.FindObjectsOfType<FireballImpactFX>().Length;
            fb.OnImpact(Vector3.zero);
            fb.OnImpact(new Vector3(5f, 5f, 0f));
            int afterRepeats = Object.FindObjectsOfType<FireballImpactFX>().Length;
            Assert.AreEqual(afterFirst, afterRepeats,
                "Subsequent OnImpact calls must be no-ops (idempotent gate via _impacted flag)");
        }

        [Test]
        public void OnImpact_AfterReEnable_GateIsReset()
        {
            // OnEnable() resets the gate so pooled fireballs work across reuses.
            var fb = CreateVisual();
            fb.OnImpact(Vector3.zero);
            int afterFirst = Object.FindObjectsOfType<FireballImpactFX>().Length;

            // Simulate pool despawn -> respawn. EditMode does not always fire
            // OnDisable/OnEnable on SetActive when the component was bootstrapped via
            // direct AddComponent in tests, so invoke them explicitly.
            InvokePrivate(fb, "OnDisable");
            InvokePrivate(fb, "OnEnable");
            fb.OnImpact(Vector3.zero);
            int afterRespawn = Object.FindObjectsOfType<FireballImpactFX>().Length;

            Assert.AreEqual(afterFirst + 1, afterRespawn,
                "After OnDisable→OnEnable cycle, OnImpact should fire again (pool reuse)");
        }

        // ── Pool-safe disable ────────────────────────────────────────

        [Test]
        public void OnDisable_DestroysDynamicLight2DChild()
        {
            // FireballLight is created only when URP Light2D type resolves; if URP isn't
            // referenced by the test runtime, the property is null and the test is a no-op.
            var fb = CreateVisual();
            var light = fb.transform.Find("FireballLight");
            if (light == null)
            {
                Assert.Pass("URP Light2D not present in this assembly — skipping cleanup check.");
                return;
            }
            // EditMode does not reliably fire OnDisable on SetActive(false) for
            // components added via AddComponent in test code; invoke directly.
            InvokePrivate(fb, "OnDisable");

            // The reference must be cleared...
            var field = typeof(FireballVisual).GetField("_light2DGo",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            Assert.IsNull(field.GetValue(fb), "_light2DGo reference must be cleared on disable");

            // ...and the child must actually be gone. This assertion was impossible while
            // OnDisable called Destroy(), which edit mode refuses to honour; SafeDestroy.Of
            // takes the DestroyImmediate branch outside play mode, so the pooled visual no
            // longer leaks one Light2D child per despawn.
            Assert.IsTrue(fb.transform.Find("FireballLight") == null,
                "The dynamic Light2D child must be destroyed, not just unreferenced.");
        }

        // ── Reflection plumbing ──────────────────────────────────────

        [Test]
        public void Light2DReflection_ResolvesConsistently()
        {
            // Whatever the result (URP present or not), the Type and property accessors
            // must agree across calls — i.e. resolution is cached.
            var t1 = FireballVisual.GetLight2DType();
            var t2 = FireballVisual.GetLight2DType();
            Assert.AreSame(t1, t2, "Light2D type lookup must be cached");

            var i1 = FireballVisual.GetLight2DIntensityProp();
            var i2 = FireballVisual.GetLight2DIntensityProp();
            Assert.AreSame(i1, i2, "Light2D intensity PropertyInfo must be cached");
        }

        // ── Material aliasing safety ─────────────────────────────────

        [Test]
        public void AllChildRenderers_ShareSingleMaterialInstance()
        {
            var fb = CreateVisual();
            var renderers = fb.GetComponentsInChildren<SpriteRenderer>(includeInactive: true)
                              .Where(r => r.gameObject != fb.gameObject) // exclude root placeholder
                              .ToArray();
            Assert.Greater(renderers.Length, 0);
            var shared = FireballVisual.SharedUnlitMaterial;
            foreach (var r in renderers)
                Assert.AreSame(shared, r.sharedMaterial,
                    $"{r.gameObject.name} must reference the shared material (no per-instance copies)");
        }
    }
}
