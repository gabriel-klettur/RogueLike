using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Exercises the <see cref="ParticlesRuntimeEditor"/> (F1) host-class lifecycle.
    ///
    /// Tests the IGameEditor contract (EditorName, IsActive), Activate/Deactivate,
    /// TUTORIAL_STEPS count, and singleton cleanup between tests.
    /// </summary>
    [TestFixture]
    public class ParticlesEditorLifecycleTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var f = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public |
                    BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetFieldValue(object obj, string name)
            => FindField(obj, name)?.GetValue(obj);

        private static void SetFieldValue(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static void InvokeMethod(object obj, string methodName, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        /// <summary>
        /// Creates a minimal catalog with two presets (no VFX params required for lifecycle tests).
        /// </summary>
        private ParticlePresetCatalog MakeCatalog(params string[] ids)
        {
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            var presets = new List<ParticlePresetDefinition>();
            foreach (var id in ids)
            {
                var def = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
                def.id          = id;
                def.displayName = id;
                presets.Add(def);
            }
            catalog.SetPresets(presets);
            return catalog;
        }

        /// <summary>Creates a <see cref="ParticlesRuntimeEditor"/> in EditMode (no Play Mode needed).</summary>
        private ParticlesRuntimeEditor CreateEditor(bool withUI = false)
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();

            var go = new GameObject("TestParticlesEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();

            // Force OnSingletonAwake so the toggle action is initialized.
            InvokeMethod(editor, "OnSingletonAwake");

            // Assign a minimal catalog via reflection so RefreshPicker doesn't complain.
            var catalog = MakeCatalog("preset_a", "preset_b");
            SetFieldValue(editor, "_catalog", catalog);

            // Stub the preview service: mark it as already initialized so that
            // Activate() → _previewService.Initialize() does nothing.
            // This prevents Camera + RenderTexture creation in EditMode, which
            // requires URP and GPU resources unavailable without PlayMode.
            StubPreviewService(editor);

            if (withUI)
                InvokeMethod(editor, "Start");

            return editor;
        }

        /// <summary>
        /// Stubs out the <see cref="ParticlePreviewService"/> inside <paramref name="editor"/>
        /// so that all its public methods are safe to call in EditMode (no Camera,
        /// no RenderTexture, no GPU resources created).
        ///
        /// Strategy:
        ///   1. Mark _initialized = true so Initialize() returns immediately (early-out).
        ///   2. Pre-populate the _pool array with empty ThumbSlot objects so that
        ///      SetVisiblePresets() can traverse the pool without NullReferenceException.
        ///      All ThumbSlot fields (RT, EmitterGo, Emitter) remain null — the production
        ///      code guards against null in SafeApplyPreset and SetLayerRecursive.
        ///   3. Assign a no-op Camera stub is not needed because Shutdown() guards
        ///      against null camera before Destroying it.
        /// </summary>
        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            if (serviceField == null) return;

            var service = serviceField.GetValue(editor);
            if (service == null) return;

            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;

            // Step 1: Mark as initialized (Initialize() will early-return).
            var initField = serviceType.GetField("_initialized", bf);
            initField?.SetValue(service, true);

            // Step 2: Pre-populate the pool array with empty ThumbSlot instances.
            // ThumbSlot is a private sealed nested class; create via Activator.
            var poolField = serviceType.GetField("_pool", bf);
            if (poolField != null)
            {
                var pool = poolField.GetValue(service) as System.Array;
                if (pool != null)
                {
                    // Locate the ThumbSlot nested type.
                    var thumbSlotType = serviceType.GetNestedType(
                        "ThumbSlot", BindingFlags.NonPublic);
                    if (thumbSlotType != null)
                    {
                        for (int i = 0; i < pool.Length; i++)
                            if (pool.GetValue(i) == null)
                                pool.SetValue(System.Activator.CreateInstance(thumbSlotType), i);
                    }
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── IGameEditor contract ─────────────────────────────────────────────────

        [Test]
        public void EditorName_Returns_ParticlesEditorString()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();

            Assert.AreEqual("Particles Editor", editor.EditorName,
                "EditorName must match the canonical display string.");
        }

        [Test]
        public void IsActive_InitiallyFalse()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();

            Assert.IsFalse(editor.IsActive, "Editor must start inactive.");
        }

        [Test]
        public void Implements_IGameEditor_Interface()
        {
            Assert.IsTrue(
                typeof(GameEditorManager.IGameEditor).IsAssignableFrom(typeof(ParticlesRuntimeEditor)),
                "ParticlesRuntimeEditor must implement IGameEditor.");
        }

        // ── Activate / Deactivate ────────────────────────────────────────────────

        [Test]
        public void Activate_Sets_IsActive_True_And_Shows_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);

            editor.Activate();

            Assert.IsTrue(editor.IsActive, "IsActive must be true after Activate().");
            var root = (GameObject) GetFieldValue(editor, "_root");
            Assert.IsTrue(root != null && root.activeSelf, "Root must be visible after Activate().");
        }

        [Test]
        public void Deactivate_Sets_IsActive_False_And_Hides_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);
            editor.Activate();

            editor.Deactivate();

            Assert.IsFalse(editor.IsActive, "IsActive must be false after Deactivate().");
            var root = (GameObject) GetFieldValue(editor, "_root");
            Assert.IsFalse(root.activeSelf, "Root must be hidden after Deactivate().");
        }

        [Test]
        public void Deactivate_Clears_SelectedPresetId()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);
            editor.Activate();
            SetFieldValue(editor, "_selectedPresetId", "aura_test");

            editor.Deactivate();

            Assert.IsNull(GetFieldValue(editor, "_selectedPresetId"),
                "Deactivate must reset _selectedPresetId.");
        }

        [Test]
        public void ToggleActive_Flips_IsActive_Twice()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);

            InvokeMethod(editor, "ToggleActive");
            Assert.IsTrue(editor.IsActive, "First toggle must activate.");

            InvokeMethod(editor, "ToggleActive");
            Assert.IsFalse(editor.IsActive, "Second toggle must deactivate.");
        }

        // ── Tutorial smoke test ──────────────────────────────────────────────────

        [Test]
        public void TutorialSteps_Has_Eight_Entries()
        {
            // Access the static readonly array via reflection.
            var field = typeof(ParticlesRuntimeEditor).GetField(
                "TUTORIAL_STEPS",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(field, "TUTORIAL_STEPS static field must exist on ParticlesRuntimeEditor.");

            var steps = field.GetValue(null) as Array;
            Assert.IsNotNull(steps, "TUTORIAL_STEPS must be a non-null array.");
            Assert.AreEqual(8, steps.Length, "TUTORIAL_STEPS must have exactly 8 entries (Python parity).");
        }

        [Test]
        public void TutorialOverlay_StartsHidden_AfterBuildUI()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);

            var tut = (GameObject) GetFieldValue(editor, "_tutorialRoot");
            Assert.IsNotNull(tut, "Tutorial root must be built by Start().");
            Assert.IsFalse(tut.activeSelf, "Tutorial must start hidden.");
        }

        [Test]
        public void ToggleTutorial_FlipsActiveState()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor(withUI: true);
            var tut = (GameObject) GetFieldValue(editor, "_tutorialRoot");

            Assert.IsFalse(tut.activeSelf, "Tutorial starts hidden.");
            InvokeMethod(editor, "ToggleTutorial");
            Assert.IsTrue(tut.activeSelf, "First toggle must show tutorial.");
            InvokeMethod(editor, "ToggleTutorial");
            Assert.IsFalse(tut.activeSelf, "Second toggle must hide tutorial.");
        }
    }
}
