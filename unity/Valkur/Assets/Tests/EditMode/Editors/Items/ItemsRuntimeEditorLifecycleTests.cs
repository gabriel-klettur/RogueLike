using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Valkur.Gameplay.Items;

namespace Valkur.Tests.EditMode.Editors.Items
{
    /// <summary>
    /// Lifecycle / build regression tests for <see cref="ItemsRuntimeEditor"/> (F7).
    ///
    /// Background — bug history:
    ///   • Items Editor was eagerly building its UI in <c>Start()</c> and was visible
    ///     for one frame at scene load.  Fixed by adopting the lazy-build pattern from
    ///     <c>BuildingsRuntimeEditor</c> (BuildUI deferred until first Activate, guarded
    ///     by <c>_uiBuilt</c>).
    ///   • <c>BuildUI()</c> then threw <c>NullReferenceException</c> at
    ///     <c>ItemsEditorUIBuilder.Panels.cs:73</c> because
    ///     <c>EditorUIHelpers.MakeScrollView</c> only adds a <c>LayoutElement</c> when an
    ///     explicit height is passed.  Fixed by introducing
    ///     <c>EnsureFlexibleHeight(go)</c> that adds the component if missing.
    ///
    /// These tests lock both regressions in place.
    /// </summary>
    [TestFixture]
    public class ItemsRuntimeEditorLifecycleTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene) if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            ClearSingletonInstance<ItemsRuntimeEditor>();
        }

        // ── Helpers ────────────────────────────────────────────────────────────────

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

        private static void Invoke(object obj, string method)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public |
                                            BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, null); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        private ItemsRuntimeEditor CreateEditor()
        {
            ClearSingletonInstance<ItemsRuntimeEditor>();
            var go = new GameObject("TestItemsEditor");
            _scene.Add(go);
            var ed = go.AddComponent<ItemsRuntimeEditor>();
            // EditMode does not run Awake/Start automatically — invoke them.
            Invoke(ed, "OnSingletonAwake");
            Invoke(ed, "Start");
            return ed;
        }

        // ── Tests ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Regression for the "visible at startup" bug: after Start, BuildUI must NOT
        /// have run, _uiBuilt must be false, and no canvas/root must exist.
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
        /// Regression for the BuildUI NRE at Panels.cs:73:
        /// EnsureFlexibleHeight must allow BuildUI to complete without throwing,
        /// and the editor must end up active with a built UI.
        /// </summary>
        [Test]
        public void Activate_BuildsUIWithoutThrowing()
        {
            // BuildUI creates Image/TMP/etc that emit UI warnings in EditMode.
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();

            Assert.DoesNotThrow(() => ed.Activate(),
                "Activate must not throw — EnsureFlexibleHeight should defend against MakeScrollView returning a ScrollRect without LayoutElement.");

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
        /// Toggling Activate → Deactivate must hide the root without throwing
        /// and leave _uiBuilt true (UI is reused).
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
        /// Re-activating after deactivate must not rebuild the UI (cheap toggle).
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

            Assert.AreSame(firstRoot, secondRoot,    "_root must be reused on second Activate.");
            Assert.AreSame(firstCanvas, secondCanvas, "_canvas must be reused on second Activate.");
            Assert.IsTrue(secondRoot.activeSelf, "_root must be active again.");
        }

        /// <summary>
        /// Sanity: every panel's ScrollRect must have a LayoutElement after BuildUI.
        /// This is exactly what EnsureFlexibleHeight guarantees and what blew up
        /// before the fix.
        /// </summary>
        [Test]
        public void Activate_AllScrollRects_HaveLayoutElement()
        {
            LogAssert.ignoreFailingMessages = true;

            var ed = CreateEditor();
            ed.Activate();

            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must exist.");

            var scrolls = root.GetComponentsInChildren<ScrollRect>(includeInactive: true);
            Assert.Greater(scrolls.Length, 0, "BuildUI must create at least one ScrollRect.");
            foreach (var sr in scrolls)
            {
                var le = sr.GetComponent<LayoutElement>();
                Assert.IsTrue(le != null,
                    $"ScrollRect '{sr.name}' must have a LayoutElement (regression — Panels.cs NRE fix).");
            }
        }
    }
}
