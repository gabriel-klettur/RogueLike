using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.Spells;

namespace Valkur.Tests.EditMode.Editors.Spells
{
    /// <summary>
    /// Unit tests for <see cref="SpellPreviewService"/> — the engine behind the
    /// Spells Editor F4 → "View" live preview panel.
    ///
    /// The service is plain C# (not a MonoBehaviour) but Initialize() spawns Unity
    /// GameObjects, a Camera, and a RenderTexture. EditMode is fine for that; we
    /// just need to clean up after each test so the scene doesn't accumulate
    /// stage roots / dummy cameras.
    /// </summary>
    [TestFixture]
    public class SpellPreviewServiceTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();
        private SpellPreviewService _service;

        [SetUp]
        public void SetUp()
        {
            // The service logs a warning the first time the SpellPreview layer is
            // missing in non-Valkur projects. The CI Valkur scene has it; just
            // tolerate any URP / canvas chatter in EditMode for safety.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Shutdown();
            _service = null;
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
        }

        private GameObject NewParent()
        {
            var go = new GameObject("PreviewParent");
            _scene.Add(go);
            return go;
        }

        private SpellDefinition NewSpell(string key = "preview_test", SpellType type = SpellType.Projectile,
            float lifetime = 2f, float duration = 0f, float prepare = 0f, float channel = 0f)
        {
            var s = ScriptableObject.CreateInstance<SpellDefinition>();
            s.spellKey         = key;
            s.displayName      = key;
            s.type             = type;
            s.prepareDuration  = prepare;
            s.channelDuration  = channel;
            s.cooldownDuration = 1f;
            s.lifetime         = lifetime;
            s.duration         = duration;
            s.damage           = 1f;
            s.speed            = 5f;
            return s;
        }

        // ── Initialize / Shutdown ────────────────────────────────────────────────

        [Test]
        public void Initialize_CreatesCameraAndRenderTexture()
        {
            _service = new SpellPreviewService();
            var parent = NewParent();

            _service.Initialize(parent.transform);

            var rt = _service.GetPreviewTexture();
            Assert.IsNotNull(rt, "Initialize must allocate the preview RenderTexture.");
            Assert.IsTrue(rt.IsCreated(), "RenderTexture must be created (not just declared).");
            Assert.AreEqual(384, rt.width, "RT width is the documented 384 px.");
            Assert.AreEqual(384, rt.height, "RT height is the documented 384 px.");
        }

        [Test]
        public void Initialize_IsIdempotent()
        {
            _service = new SpellPreviewService();
            var parent = NewParent();

            _service.Initialize(parent.transform);
            var firstRT = _service.GetPreviewTexture();
            _service.Initialize(parent.transform);
            var secondRT = _service.GetPreviewTexture();

            Assert.AreSame(firstRT, secondRT,
                "A second Initialize must not allocate a fresh RT (would leak the first one).");
        }

        [Test]
        public void Shutdown_ReleasesRenderTexture_AndIsSafeToCallTwice()
        {
            _service = new SpellPreviewService();
            var parent = NewParent();
            _service.Initialize(parent.transform);

            Assert.DoesNotThrow(() => _service.Shutdown());
            Assert.IsNull(_service.GetPreviewTexture(),
                "GetPreviewTexture must return null after Shutdown.");

            // Calling shutdown a second time must be a no-op, not throw.
            Assert.DoesNotThrow(() => _service.Shutdown(),
                "Shutdown must be safe to call repeatedly (called from both Deactivate and OnDestroy).");
        }

        // ── Open / Close ─────────────────────────────────────────────────────────

        [Test]
        public void Open_EnablesCamera_Close_DisablesCamera()
        {
            _service = new SpellPreviewService();
            var parent = NewParent();
            _service.Initialize(parent.transform);

            var camera = parent.GetComponentInChildren<Camera>(includeInactive: true);
            Assert.IsNotNull(camera, "A preview camera must be created under the parent.");
            Assert.IsFalse(camera.enabled,
                "Camera must start disabled — only render while the View panel is open.");

            _service.Open();
            Assert.IsTrue(camera.enabled, "Open must enable the camera.");

            _service.Close();
            Assert.IsFalse(camera.enabled, "Close must disable the camera (saves a draw).");
        }

        // ── Direction ────────────────────────────────────────────────────────────

        [Test]
        public void SetDirection_NormalizesNonZeroVectors()
        {
            _service = new SpellPreviewService();
            _service.Initialize(NewParent().transform);

            _service.SetDirection(new Vector2(3f, 4f));   // length 5
            var dir = (Vector2)Field(_service, "_direction").GetValue(_service);
            Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-4f),
                "SetDirection must normalize the input.");
        }

        [Test]
        public void SetDirection_IgnoresZeroVector()
        {
            _service = new SpellPreviewService();
            _service.Initialize(NewParent().transform);

            _service.SetDirection(Vector2.right);
            _service.SetDirection(Vector2.zero);          // must not blank the direction
            var dir = (Vector2)Field(_service, "_direction").GetValue(_service);
            Assert.AreEqual(Vector2.right, dir,
                "Zero direction must be ignored — last valid direction kept.");
        }

        // ── Zoom ─────────────────────────────────────────────────────────────────

        [Test]
        public void Zoom_StartsAtOne()
        {
            _service = new SpellPreviewService();
            Assert.AreEqual(1f, _service.CurrentZoom,
                "Default zoom must be 1.0 (auto-fit baseline).");
        }

        [Test]
        public void ZoomIn_MultipliesByStep_ZoomOut_DividesByStep()
        {
            _service = new SpellPreviewService();
            float baseline = _service.CurrentZoom;

            _service.ZoomIn();
            Assert.Greater(_service.CurrentZoom, baseline, "ZoomIn must increase zoom.");
            float afterIn = _service.CurrentZoom;

            _service.ZoomOut();
            Assert.That(_service.CurrentZoom, Is.EqualTo(baseline).Within(1e-3f),
                "ZoomOut after ZoomIn must restore the baseline (within float epsilon).");

            _service.ZoomOut();
            Assert.Less(_service.CurrentZoom, afterIn, "Second ZoomOut must reduce further.");
        }

        [Test]
        public void SetZoom_ClampsToConfiguredRange()
        {
            _service = new SpellPreviewService();

            _service.SetZoom(0f);
            Assert.GreaterOrEqual(_service.CurrentZoom, 0.25f,
                "Zoom must be clamped to its lower bound (0.25×).");

            _service.SetZoom(9999f);
            Assert.LessOrEqual(_service.CurrentZoom, 6f,
                "Zoom must be clamped to its upper bound (6×).");
        }

        [Test]
        public void ZoomBy_AppliesContinuousDelta()
        {
            _service = new SpellPreviewService();
            float before = _service.CurrentZoom;

            _service.ZoomBy(1f);                 // exactly one step
            Assert.Greater(_service.CurrentZoom, before, "Positive ZoomBy must zoom in.");
            float positive = _service.CurrentZoom;

            _service.ZoomBy(-1f);                // exactly one step out
            Assert.That(_service.CurrentZoom, Is.EqualTo(before).Within(1e-3f),
                "ZoomBy(-1) after ZoomBy(+1) must round-trip back to baseline.");
            Assert.Less(_service.CurrentZoom, positive, "Negative ZoomBy must zoom out.");
        }

        [Test]
        public void ZoomBy_ZeroDelta_IsNoop()
        {
            _service = new SpellPreviewService();
            float before = _service.CurrentZoom;
            _service.ZoomBy(0f);
            Assert.AreEqual(before, _service.CurrentZoom);
        }

        // ── HasProjectilePrefab ──────────────────────────────────────────────────

        [Test]
        public void HasProjectilePrefab_FalseInEmptyScene()
        {
            _service = new SpellPreviewService();
            // No SpellCaster in the test scene → the resolver returns null and the
            // status label can warn the user that projectile preview is disabled.
            Assert.IsFalse(_service.HasProjectilePrefab,
                "Without a SpellCaster in the scene there is no projectile prefab to use.");
        }

        // ── Reflection helper ────────────────────────────────────────────────────

        private static FieldInfo Field(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            Assert.Fail($"Field '{name}' not found on {obj.GetType().Name}");
            return null;
        }
    }
}
