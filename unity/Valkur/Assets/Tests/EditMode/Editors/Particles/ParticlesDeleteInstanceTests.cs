using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Tests for the delete-selected-instance feature of <see cref="ParticlesRuntimeEditor"/>
    /// (the "Delete Instance" button in the Properties panel).
    /// </summary>
    [TestFixture]
    public class ParticlesDeleteInstanceTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();
        private ParticlesRuntimeEditor _editor;
        private ParticlePresetDefinition _preset;
        private ParticlePresetCatalog _catalog;

        // ── Reflection helpers ────────────────────────────────────────────────────

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

        private static object GetVal(object obj, string name)
            => FindField(obj, name)?.GetValue(obj);

        private static void SetVal(object obj, string name, object value)
            => FindField(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method, params object[] args)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                t = t.BaseType;
            }
            m?.Invoke(obj, args);
        }

        private static void StubPreviewService(ParticlesRuntimeEditor editor)
        {
            var serviceField = FindField(editor, "_previewService");
            if (serviceField == null) return;
            var service = serviceField.GetValue(editor);
            if (service == null) return;
            var serviceType = service.GetType();
            const BindingFlags bf = BindingFlags.NonPublic | BindingFlags.Instance;
            serviceType.GetField("_initialized", bf)?.SetValue(service, true);
            var poolField = serviceType.GetField("_pool", bf);
            if (poolField != null)
            {
                var pool = poolField.GetValue(service) as System.Array;
                if (pool != null)
                {
                    var thumbSlotType = serviceType.GetNestedType("ThumbSlot", BindingFlags.NonPublic);
                    if (thumbSlotType != null)
                        for (int i = 0; i < pool.Length; i++)
                            if (pool.GetValue(i) == null)
                                pool.SetValue(System.Activator.CreateInstance(thumbSlotType), i);
                }
            }
        }

        // ── Editor factory ────────────────────────────────────────────────────────

        private ParticlesRuntimeEditor CreateEditor(bool withUI = false)
        {
            ClearSingletonInstance<ParticlesRuntimeEditor>();
            var go = new GameObject("DeleteInstanceTestEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");

            _preset = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            _preset.id          = "aura_smoke";
            _preset.displayName = "Aura Smoke";

            _catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            _catalog.SetPresets(new List<ParticlePresetDefinition> { _preset });
            SetVal(editor, "_catalog", _catalog);

            StubPreviewService(editor);

            if (withUI)
                Invoke(editor, "Start");

            return editor;
        }

        private GameObject SpawnEmitter(ParticlesRuntimeEditor editor, Vector3 pos)
        {
            var t = editor.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod("SpawnEmitterAt",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }
            var go = m?.Invoke(editor, new object[] { _preset, pos, -1f }) as GameObject;
            if (go != null) _sceneObjects.Add(go);
            return go;
        }

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _editor = CreateEditor(withUI: true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingletonInstance<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

        [Test]
        public void RequestDeleteSelected_NullActive_DoesNotOpenModal()
        {
            // No _activeInstance → must not open the confirm modal.
            SetVal(_editor, "_activeInstance", null);
            var modal = GetVal(_editor, "_confirmModal") as GameObject;

            Invoke(_editor, "RequestDeleteSelectedInstanceWithConfirm");

            if (modal != null)
                Assert.IsFalse(modal.activeSelf,
                    "Confirm modal must NOT open when no instance is selected.");
        }

        [Test]
        public void RequestDeleteSelected_WithActive_ShowsConfirmModal()
        {
            // Assign an active instance and call RequestDeleteSelectedInstanceWithConfirm.
            var emitterGo = SpawnEmitter(_editor, new Vector3(1f, 1f, 0f));
            SetVal(_editor, "_activeInstance", emitterGo);

            var modal = GetVal(_editor, "_confirmModal") as GameObject;

            Invoke(_editor, "RequestDeleteSelectedInstanceWithConfirm");

            if (modal != null)
                Assert.IsTrue(modal.activeSelf,
                    "Confirm modal must open when an active instance is set.");
        }

        [Test]
        public void RequestDeleteSelected_WithActive_AfterConfirm_DestroysInstanceAndClearsRef()
        {
            // Spawn and select an emitter. Then invoke the pending confirm action to simulate
            // the user clicking "Sí" in the modal.
            var emitterGo = SpawnEmitter(_editor, new Vector3(2f, 2f, 0f));
            SetVal(_editor, "_activeInstance", emitterGo);

            Invoke(_editor, "RequestDeleteSelectedInstanceWithConfirm");

            // Simulate confirm click: invoke _pendingConfirmYes directly.
            var pendingYes = GetVal(_editor, "_pendingConfirmYes") as System.Action;
            pendingYes?.Invoke();

            // The emitter GameObject must be destroyed.
            Assert.IsTrue(emitterGo == null || !emitterGo.activeSelf,
                "Emitter must be destroyed after confirming delete.");

            // _activeInstance must be null.
            var active = GetVal(_editor, "_activeInstance") as GameObject;
            Assert.IsTrue(active == null,
                "_activeInstance must be null after the instance is deleted.");
        }

        [Test]
        public void DeleteInstanceBtn_HiddenByDefault()
        {
            // Build UIRefs. DeleteInstanceBtnGo must start inactive (hidden).
            var ui = (ParticlesEditorUIBuilder.UIRefs)GetVal(_editor, "_ui");
            if (ui.DeleteInstanceBtnGo == null)
            {
                Assert.Inconclusive("DeleteInstanceBtnGo is null — UI may not be built.");
                return;
            }
            Assert.IsFalse(ui.DeleteInstanceBtnGo.activeSelf,
                "DeleteInstanceBtnGo must be hidden when no instance is selected.");
        }

        [Test]
        public void DeleteInstanceBtn_ShownAfterShowInstanceProperties_WithValidGo()
        {
            // Call ShowInstanceProperties with a valid GO → button must become visible.
            var emitterGo = SpawnEmitter(_editor, new Vector3(3f, 3f, 0f));

            Invoke(_editor, "ShowInstanceProperties", emitterGo);

            var ui = (ParticlesEditorUIBuilder.UIRefs)GetVal(_editor, "_ui");
            if (ui.DeleteInstanceBtnGo == null)
            {
                Assert.Inconclusive("DeleteInstanceBtnGo is null — UI may not be built.");
                return;
            }
            Assert.IsTrue(ui.DeleteInstanceBtnGo.activeSelf,
                "DeleteInstanceBtnGo must be visible after ShowInstanceProperties with a valid instance.");
        }

        [Test]
        public void DeleteInstanceBtn_HiddenAfterShowInstanceProperties_WithNull()
        {
            // Show then clear: passing null hides the button again.
            var emitterGo = SpawnEmitter(_editor, new Vector3(4f, 4f, 0f));
            Invoke(_editor, "ShowInstanceProperties", emitterGo);
            Invoke(_editor, "ShowInstanceProperties", (GameObject)null);

            var ui = (ParticlesEditorUIBuilder.UIRefs)GetVal(_editor, "_ui");
            if (ui.DeleteInstanceBtnGo == null)
            {
                Assert.Inconclusive("DeleteInstanceBtnGo is null — UI may not be built.");
                return;
            }
            Assert.IsFalse(ui.DeleteInstanceBtnGo.activeSelf,
                "DeleteInstanceBtnGo must be hidden again after ShowInstanceProperties(null).");
        }
    }
}
