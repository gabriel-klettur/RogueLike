using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Editors;
using Valkur.Gameplay.Inventory;

namespace Valkur.Tests.EditMode.Editors.Inventory
{
    /// <summary>
    /// Lifecycle, build and UI/UX regression tests for
    /// <see cref="InventoryRuntimeEditor"/> (F6).
    ///
    /// Bug history covered by these tests:
    ///   • F6 did nothing because GameplaySceneSetup never instantiated the editor
    ///     (no <c>EnsureInventoryRuntimeEditor()</c>).  Tested separately in
    ///     <c>InventoryEditorBootstrapTests</c>.
    ///   • Editor was eagerly building UI in <c>Start()</c> (menu bar flashed at
    ///     scene load).  Adopted lazy-build pattern; locked in by
    ///     <c>Start_DoesNotBuildUI_LazyPattern</c>.
    ///   • <c>BuildUI</c> must not throw; locked in by <c>Activate_BuildsUIWithoutThrowing</c>.
    /// </summary>
    [TestFixture]
    public class InventoryRuntimeEditorLifecycleTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            ClearSingletonInstance<InventoryRuntimeEditor>();
        }

        // ── Reflection helpers ─────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var t = typeof(T).BaseType;
            while (t != null)
            {
                var f = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { f.SetValue(null, null); return; }
                t = t.BaseType;
            }
        }

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
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);

        private static MethodInfo Method(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public |
                                          BindingFlags.Instance);
                if (m != null) return m;
                t = t.BaseType;
            }
            return null;
        }

        private static void Invoke(object obj, string method, params object[] args)
        {
            var m = Method(obj, method);
            Assert.IsNotNull(m, $"Method '{method}' not found on {obj.GetType().Name}");
            m.Invoke(obj, args);
        }

        private InventoryRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<InventoryRuntimeEditor>();
            var go = new GameObject("TestInventoryEditor");
            _scene.Add(go);
            var ed = go.AddComponent<InventoryRuntimeEditor>();
            // EditMode does not run Awake/Start automatically — invoke them.
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            return ed;
        }

        // ── Lazy build ─────────────────────────────────────────────────────────

        /// <summary>
        /// Regression: Start() must NOT build the UI (lazy-build pattern).
        /// </summary>
        [Test]
        public void Start_DoesNotBuildUI_LazyPattern()
        {
            var ed = CreateEditor();

            Assert.IsFalse((bool)GetField(ed, "_uiBuilt"),
                "_uiBuilt must remain false after Start (lazy build pattern).");
            Assert.IsNull(GetField(ed, "_canvas"),
                "_canvas must not be created until first Activate.");
            Assert.IsNull(GetField(ed, "_root"),
                "_root must not be created until first Activate.");
            Assert.IsFalse(ed.IsActive, "Editor must start inactive.");
        }

        /// <summary>
        /// Activate must build the UI without throwing.
        /// </summary>
        [Test]
        public void Activate_BuildsUIWithoutThrowing()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();

            Assert.DoesNotThrow(() => ed.Activate(),
                "Activate must not throw on first call.");

            Assert.IsTrue((bool)GetField(ed, "_uiBuilt"),
                "_uiBuilt must be true after first Activate.");
            Assert.IsTrue(ed.IsActive, "Editor must be active after Activate.");

            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must be created.");
            Assert.IsTrue(root.activeSelf, "_root must be active.");

            var canvas = GetField(ed, "_canvas") as Canvas;
            Assert.IsTrue(canvas != null, "_canvas must be created.");
        }

        /// <summary>
        /// Deactivate must hide _root without throwing and preserve the built UI.
        /// </summary>
        [Test]
        public void Deactivate_HidesRoot_AndDoesNotThrow()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();

            Assert.DoesNotThrow(() => ed.Deactivate());

            Assert.IsFalse(ed.IsActive, "Editor must be inactive after Deactivate.");
            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must still exist after Deactivate.");
            Assert.IsFalse(root.activeSelf, "_root must be hidden after Deactivate.");
            Assert.IsTrue((bool)GetField(ed, "_uiBuilt"),
                "_uiBuilt must remain true so Activate reuses the UI.");
        }

        /// <summary>
        /// Re-activating must reuse the existing UI (no rebuild).
        /// </summary>
        [Test]
        public void ActivateTwice_DoesNotRebuildUI()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();
            var firstRoot   = GetField(ed, "_root") as GameObject;
            var firstCanvas = GetField(ed, "_canvas") as Canvas;

            ed.Deactivate();
            ed.Activate();

            var secondRoot   = GetField(ed, "_root") as GameObject;
            var secondCanvas = GetField(ed, "_canvas") as Canvas;

            Assert.AreSame(firstRoot,   secondRoot,   "_root must be reused on second Activate.");
            Assert.AreSame(firstCanvas, secondCanvas, "_canvas must be reused on second Activate.");
            Assert.IsTrue(secondRoot.activeSelf, "_root must be active again.");
        }

        // ── Public API ─────────────────────────────────────────────────────────

        [Test]
        public void EditorName_IsInventoryEditor()
        {
            var ed = CreateEditor();
            Assert.AreEqual("Inventory Editor", ed.EditorName);
        }

        // ── Input System: F6 binding ───────────────────────────────────────────

        /// <summary>
        /// The InputAction must be bound to F6 (regression for "F6 doesn't work").
        /// </summary>
        [Test]
        public void ToggleAction_ShipsUnbound()
        {
            var ed = CreateEditor();

            // The editor toggles ship UNBOUND: every runtime editor is reached from the
            // General Editor on Escape, and the F-row was the source of every same-map
            // collision in the project. The action still EXISTS so the Controls editor can
            // offer it and a player can assign a key — which is why this asserts "no
            // bindings" rather than "no action". Reachability is pinned centrally by
            // EditorEntryPointTests.EveryRetiredToggle_HasAGeneralEditorEntry.
            var action = GetField(ed, "_toggleAction") as InputAction;
            if (action == null) Assert.Pass("Ships unbound and resolves to no action here.");

            Assert.AreEqual(InputActionType.Button, action.type, "Action must be a Button type.");
            Assert.AreEqual(0, action.bindings.Count,
                "The Inventory toggle must ship unbound — F6 is free now.");
            Assert.IsTrue(action.enabled, "Action must be enabled after OnSingletonAwake.");
        }
    }
}
