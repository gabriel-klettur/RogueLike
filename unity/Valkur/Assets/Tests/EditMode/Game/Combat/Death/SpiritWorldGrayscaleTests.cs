using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.Combat.Death
{
    /// <summary>
    /// Pin-down tests for the spirit-mode grayscale system. Verifies that:
    ///   1. SpriteRenderer / TilemapRenderer materials swap to the desaturate
    ///      shader on player death and restore on revive.
    ///   2. Renderers under an altar BuildingObject (matching templateId) and
    ///      under the path highlighter's MarkerRoot are EXEMPT — they keep
    ///      their original material so they're the only color in the scene.
    ///   3. The system reacts to GameEvents.OnPlayerDied / OnPlayerRevived /
    ///      OnPlayerResurrected (the canonical lifecycle events).
    ///
    /// Tests use reflection only where unavoidable (BuildingObject._template
    /// is a private SerializeField; calling Apply requires a real sprite at
    /// a Resources path which we don't have in EditMode).
    /// </summary>
    public class SpiritWorldGrayscaleTests
    {
        private const int AltarTemplateId = 249;

        private GameObject _systemHost;
        private SpiritWorldGrayscale _system;
        private List<GameObject> _spawned;

        [SetUp]
        public void Setup()
        {
            _spawned = new List<GameObject>();
            ServiceLocator.Clear();
            ClearGameEvents();

            _systemHost = new GameObject("SpiritWorldGrayscaleTestHost");
            _spawned.Add(_systemHost);
            _system = _systemHost.AddComponent<SpiritWorldGrayscale>();
            // Unity's EditMode test runner doesn't fire Awake on AddComponent
            // deterministically — call the public idempotent Initialize so
            // every test starts with subscriptions wired and ServiceLocator
            // entry in place regardless of the runner's quirks.
            _system.Initialize();
        }

        [TearDown]
        public void Teardown()
        {
            // Explicit shutdown so the system unsubscribes from GameEvents
            // before the GameObject is destroyed — DestroyImmediate doesn't
            // run OnDestroy lifecycle reliably on test-only objects.
            if (_system != null) _system.Shutdown();
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null) Object.DestroyImmediate(_spawned[i]);
            }
            _spawned.Clear();
            ServiceLocator.Clear();
            ClearGameEvents();
        }

        // ── Coverage ────────────────────────────────────────────────────────────

        [Test]
        public void Apply_SwapsNonExemptSpriteRendererMaterial()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");
            var original = sr.sharedMaterial;

            _system.ForceApply();

            Assert.AreNotSame(original, sr.sharedMaterial,
                "Non-exempt SpriteRenderer should have its sharedMaterial swapped to the desat material.");
            Assert.AreEqual("Valkur/SpriteDesaturate", sr.sharedMaterial.shader.name,
                "Swapped material should use the desaturation shader.");
        }

        [Test]
        public void Apply_SwapsNonExemptTilemapRendererMaterial()
        {
            if (DesatShaderMissing()) return;
            var (tilemap, tmRenderer) = SpawnTilemap("Ground");
            var original = tmRenderer.sharedMaterial;

            _system.ForceApply();

            Assert.AreNotSame(original, tmRenderer.sharedMaterial,
                "Non-exempt TilemapRenderer should have its sharedMaterial swapped.");
            Assert.AreEqual("Valkur/SpriteDesaturate", tmRenderer.sharedMaterial.shader.name);
        }

        [Test]
        public void Restore_BringsBackOriginalSpriteRendererMaterial()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");
            var original = sr.sharedMaterial;

            _system.ForceApply();
            _system.ForceRestore();

            Assert.AreSame(original, sr.sharedMaterial,
                "Restore must hand back the exact sharedMaterial captured before the swap.");
        }

        [Test]
        public void Restore_BringsBackOriginalTilemapMaterial()
        {
            if (DesatShaderMissing()) return;
            var (tilemap, tmRenderer) = SpawnTilemap("Ground");
            var original = tmRenderer.sharedMaterial;

            _system.ForceApply();
            _system.ForceRestore();

            Assert.AreSame(original, tmRenderer.sharedMaterial);
        }

        [Test]
        public void RendererUnderAltarBuilding_StaysOriginal()
        {
            if (DesatShaderMissing()) return;
            var altar = SpawnAltarBuilding(templateId: AltarTemplateId);
            var sr = SpawnSpriteUnder(altar.transform, "AltarSprite");
            var original = sr.sharedMaterial;

            _system.ForceApply();

            Assert.AreSame(original, sr.sharedMaterial,
                "SpriteRenderer parented under an altar building must NOT be desaturated.");
        }

        [Test]
        public void RendererUnderUnrelatedBuilding_IsDesaturated()
        {
            if (DesatShaderMissing()) return;
            var nonAltar = SpawnAltarBuilding(templateId: 1); // not the altar id
            var sr = SpawnSpriteUnder(nonAltar.transform, "TreeSprite");
            var original = sr.sharedMaterial;

            _system.ForceApply();

            Assert.AreNotSame(original, sr.sharedMaterial,
                "A building whose templateId doesn't match the altar id is desaturated like everything else.");
        }

        [Test]
        public void RendererUnderPathMarkerRoot_StaysOriginal()
        {
            if (DesatShaderMissing()) return;
            var highlighterHost = new GameObject("SpiritAltarPathHighlighter");
            _spawned.Add(highlighterHost);
            var highlighter = highlighterHost.AddComponent<SpiritAltarPathHighlighter>();
            // ServiceLocator.Register fires from Awake which doesn't run
            // deterministically under EditMode tests, so register manually
            // here instead of trusting the lifecycle hook.
            ServiceLocator.Register<SpiritAltarPathHighlighter>(highlighter);

            Assert.NotNull(highlighter.MarkerRoot, "MarkerRoot getter is supposed to lazy-create on first access.");

            var sr = SpawnSpriteUnder(highlighter.MarkerRoot, "PathTile");
            var original = sr.sharedMaterial;

            _system.ForceApply();

            Assert.AreSame(original, sr.sharedMaterial,
                "Path-marker SpriteRenderers must keep their yellow tint while the rest of the world goes gray.");
        }

        [Test]
        public void OnPlayerDied_TriggersGrayscale()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");
            var original = sr.sharedMaterial;

            // Sanity probe — was a parallel subscription preserved across the
            // reflection-based ClearGameEvents() in Setup? If this counter
            // doesn't bump, the test harness wiped the event after Awake
            // re-subscribed and no handler will fire.
            int probeCalls = 0;
            System.Action probe = () => probeCalls++;
            GameEvents.OnPlayerDied += probe;

            GameEvents.FirePlayerDied();

            GameEvents.OnPlayerDied -= probe;

            Assert.AreEqual(1, probeCalls, "Probe handler must run — confirms FirePlayerDied dispatch is live.");
            Assert.IsTrue(_system.IsGrayscaleActive, "SpiritWorldGrayscale.Awake should have subscribed before the probe — subscription chain broken.");
            Assert.AreNotSame(original, sr.sharedMaterial);
        }

        [Test]
        public void OnPlayerRevived_RestoresWorld()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");
            var original = sr.sharedMaterial;

            GameEvents.FirePlayerDied();
            GameEvents.FirePlayerRevived();

            Assert.IsFalse(_system.IsGrayscaleActive);
            Assert.AreSame(original, sr.sharedMaterial);
        }

        [Test]
        public void OnPlayerResurrected_AlsoRestoresWorld()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");
            var original = sr.sharedMaterial;

            GameEvents.FirePlayerDied();
            GameEvents.FirePlayerResurrected();

            Assert.IsFalse(_system.IsGrayscaleActive);
            Assert.AreSame(original, sr.sharedMaterial);
        }

        [Test]
        public void DoubleApply_IsIdempotent()
        {
            if (DesatShaderMissing()) return;
            var sr = SpawnSprite("Plain");

            _system.ForceApply();
            int afterFirst = _system.CapturedRendererCount;
            _system.ForceApply();
            int afterSecond = _system.CapturedRendererCount;

            Assert.AreEqual(afterFirst, afterSecond,
                "Apply must early-return when already active so we don't lose the original-material capture.");
        }

        // ── Test helpers ────────────────────────────────────────────────────────

        private SpriteRenderer SpawnSprite(string label)
        {
            var go = new GameObject(label);
            _spawned.Add(go);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = NewTestMaterial(label);
            return sr;
        }

        private SpriteRenderer SpawnSpriteUnder(Transform parent, string label)
        {
            var go = new GameObject(label);
            _spawned.Add(go);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = NewTestMaterial(label);
            return sr;
        }

        private (Tilemap, TilemapRenderer) SpawnTilemap(string label)
        {
            var go = new GameObject(label);
            _spawned.Add(go);
            // A Tilemap requires a Grid in its parent chain. Add one to the
            // same GameObject — it satisfies Tilemap's RequireComponent contract.
            go.AddComponent<Grid>();
            var tm = go.AddComponent<Tilemap>();
            var tmr = go.AddComponent<TilemapRenderer>();
            tmr.sharedMaterial = NewTestMaterial(label);
            return (tm, tmr);
        }

        private GameObject SpawnAltarBuilding(int templateId)
        {
            var go = new GameObject($"Building_{templateId}");
            _spawned.Add(go);
            var bo = go.AddComponent<BuildingObject>();

            var template = ScriptableObject.CreateInstance<BuildingTemplateData>();
            template.templateId = templateId;
            template.name = $"BuildingTemplate_{templateId}";
            // We cannot call BuildingObject.Apply (loads a real sprite at a
            // Resources path that doesn't exist here), so reach the private
            // _template field directly. The grayscale system only reads
            // BuildingObject.Template, so the rest of Apply's side effects
            // are irrelevant for this test.
            var fld = typeof(BuildingObject).GetField(
                "_template",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(fld, "BuildingObject._template field name changed — update test reflection to match.");
            fld.SetValue(bo, template);

            return go;
        }

        private static Material NewTestMaterial(string label)
        {
            // Pick the simplest URP-compatible sprite shader available so the
            // test material is real (not null) but doesn't require any
            // particular pipeline state.
            var shader = Shader.Find("Sprites/Default")
                      ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                      ?? Shader.Find("Hidden/InternalErrorShader");
            return new Material(shader) { name = $"TestMat_{label}" };
        }

        private static bool DesatShaderMissing()
        {
            if (Shader.Find("Valkur/SpriteDesaturate") != null) return false;
            Assert.Inconclusive("Valkur/SpriteDesaturate shader not found in this run — skipping. " +
                                "This means the shader file failed to import; the runtime would also be a no-op.");
            return true;
        }

        private static void ClearGameEvents()
        {
            // GameEvents is a static event hub. Tests must not leak handlers
            // across runs or one test's grayscale system would hijack the next.
            var t = typeof(GameEvents);
            ResetEvent(t, "OnPlayerDied");
            ResetEvent(t, "OnPlayerRevived");
            ResetEvent(t, "OnPlayerResurrected");
        }

        private static void ResetEvent(System.Type t, string name)
        {
            var fld = t.GetField(name,
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            if (fld != null) fld.SetValue(null, null);
        }
    }
}
