using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Data;
using Valkur.Gameplay.VFX;
using Valkur.UIKit;

namespace Valkur.Tests.EditMode.Editors.Particles
{
    /// <summary>
    /// Smoke-tests the undo/redo wiring inside <see cref="ParticlesRuntimeEditor"/>.
    ///
    /// Calls <c>ExecutePersistedEdit</c> via reflection with a lambda that increments
    /// a captured counter, then exercises <see cref="UndoStack.Undo"/> and
    /// <see cref="UndoStack.Redo"/> to verify the callbacks fire correctly.
    ///
    /// Note: <c>ExecutePersistedEdit</c> also calls <c>PersistDirtyInstanceChanges</c>
    /// which tries to write to StreamingAssets. With no active instances in the scene
    /// the save will write an empty JSON array; this is benign in tests.
    /// </summary>
    [TestFixture]
    public class ParticlesUndoTests
    {
        private readonly List<GameObject> _sceneObjects = new List<GameObject>();

        // ── Reflection helpers ───────────────────────────────────────────────────

        private static void ClearSingleton<T>() where T : MonoBehaviour
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

        private static object GetVal(object obj, string name) => FindField(obj, name)?.GetValue(obj);
        private static void SetVal(object obj, string name, object value) => FindField(obj, name)?.SetValue(obj, value);

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

        private ParticlesRuntimeEditor CreateEditor()
        {
            ClearSingleton<ParticlesRuntimeEditor>();
            var go = new GameObject("UndoTestEditor");
            _sceneObjects.Add(go);
            var editor = go.AddComponent<ParticlesRuntimeEditor>();
            Invoke(editor, "OnSingletonAwake");

            // Minimal catalog so Start() / RefreshPicker() don't complain.
            var catalog = ScriptableObject.CreateInstance<ParticlePresetCatalog>();
            var preset = ScriptableObject.CreateInstance<ParticlePresetDefinition>();
            preset.id = "aura_test"; preset.displayName = "aura_test";
            catalog.SetPresets(new[] { preset });
            SetVal(editor, "_catalog", catalog);

            Invoke(editor, "Start");
            return editor;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            ClearSingleton<ParticlesRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Undo / Redo wiring ───────────────────────────────────────────────────

        [Test]
        public void ExecutePersistedEdit_Increments_Counter_OnDo()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();
            int counter = 0;

            Invoke(editor, "ExecutePersistedEdit",
                "test-do",
                (System.Action) (() => counter++),
                (System.Action) (() => counter--));

            Assert.AreEqual(1, counter,
                "ExecutePersistedEdit must call doAction immediately (counter == 1).");
        }

        [Test]
        public void Undo_After_ExecutePersistedEdit_Decrements_Counter()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();
            int counter = 0;

            Invoke(editor, "ExecutePersistedEdit",
                "test-undo",
                (System.Action) (() => counter++),
                (System.Action) (() => counter--));

            Assert.AreEqual(1, counter, "Pre-condition: counter must be 1 after Do.");

            var undoStack = GetVal(editor, "_undo") as UndoStack;
            Assert.IsNotNull(undoStack, "_undo field must be an UndoStack.");
            undoStack.Undo();

            Assert.AreEqual(0, counter,
                "After Undo(), counter must return to 0 (undoAction executed).");
        }

        [Test]
        public void Redo_After_Undo_ReIncremented_Counter()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();
            int counter = 0;

            Invoke(editor, "ExecutePersistedEdit",
                "test-redo",
                (System.Action) (() => counter++),
                (System.Action) (() => counter--));

            var undoStack = GetVal(editor, "_undo") as UndoStack;
            undoStack.Undo();
            Assert.AreEqual(0, counter, "Pre-condition: counter must be 0 after Undo.");

            undoStack.Redo();
            Assert.AreEqual(1, counter,
                "After Redo(), counter must return to 1 (doAction re-executed).");
        }

        [Test]
        public void UndoStack_Capacity_Is_64()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();

            var undoStack = GetVal(editor, "_undo") as UndoStack;
            Assert.IsNotNull(undoStack, "_undo field must be an UndoStack.");
            Assert.AreEqual(64, undoStack.Capacity,
                "UndoStack must have capacity 64 (Buildings/Entities parity).");
        }

        [Test]
        public void UndoRedoLabels_UpdateAfterExecutePersistedEdit()
        {
            LogAssert.ignoreFailingMessages = true;
            var editor = CreateEditor();
            int counter = 0;

            Invoke(editor, "ExecutePersistedEdit",
                "my-label",
                (System.Action) (() => counter++),
                (System.Action) (() => counter--));

            var ui = GetVal(editor, "_ui");
            var undoLabelField = ui.GetType().GetField("UndoBtnLabel");
            var undoLabel = undoLabelField.GetValue(ui) as TMPro.TextMeshProUGUI;

            Assert.IsNotNull(undoLabel, "UndoBtnLabel must be populated.");
            Assert.IsTrue(undoLabel.text.Contains("my-label"),
                "UndoBtnLabel text must contain the edit label after ExecutePersistedEdit.");
        }
    }
}
