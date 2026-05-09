using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Gameplay.Enemies.FSM;

namespace Valkur.Tests.EditMode.Editors.FSM
{
    /// <summary>
    /// Lifecycle, contract, and UI-build smoke tests for <see cref="FSMRuntimeEditor"/> (F12).
    ///
    /// Mirrors the canonical pattern used by ParticlesEditorLifecycleTests and
    /// ItemsRuntimeEditorLifecycleTests — the FSM editor belongs to the
    /// "abstract data editor" cohort (Particles, Spells, Entities, Map, FSM)
    /// that does NOT implement <see cref="IAllowsPlayerMovement"/>: opening it
    /// must freeze the player. The interface-marker test below pins that
    /// convention so a future "fix" can't silently regress it.
    /// </summary>
    [TestFixture]
    public class FSMRuntimeEditorLifecycleTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();
            ClearSingletonInstance<FSMRuntimeEditor>();
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers ───────────────────────────────────────────────────

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
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static object GetField(object obj, string name) => Field(obj, name)?.GetValue(obj);

        private static void SetField(object obj, string name, object value)
            => Field(obj, name)?.SetValue(obj, value);

        private static void Invoke(object obj, string method)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var m = t.GetMethod(method,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                if (m != null) { m.Invoke(obj, null); return; }
                t = t.BaseType;
            }
            Assert.Fail($"Method '{method}' not found on {obj.GetType().Name}");
        }

        /// <summary>
        /// Creates a <see cref="FSMRuntimeEditor"/> in EditMode without invoking Start
        /// (BuildUI is heavy — only call when a test needs the UI tree).
        /// </summary>
        private FSMRuntimeEditor CreateEditor(bool buildUI = false)
        {
            ClearSingletonInstance<FSMRuntimeEditor>();
            var go = new GameObject("TestFSMEditor");
            _scene.Add(go);
            var ed = go.AddComponent<FSMRuntimeEditor>();
            Invoke(ed, "OnSingletonAwake");
            if (buildUI) Invoke(ed, "Start");
            return ed;
        }

        // ── IGameEditor contract ─────────────────────────────────────────────────

        [Test]
        public void Implements_IGameEditor_Interface()
        {
            Assert.IsTrue(
                typeof(GameEditorManager.IGameEditor).IsAssignableFrom(typeof(FSMRuntimeEditor)),
                "FSMRuntimeEditor must implement GameEditorManager.IGameEditor.");
        }

        /// <summary>
        /// Pins the project-wide convention: data-catalog editors (Particles, Spells,
        /// Entities, Map, FSM) do NOT implement IAllowsPlayerMovement — opening one
        /// freezes the player. World/placement editors (Buildings, Tile, Items,
        /// Spawners, Lighting) opt-in. Adding the marker to FSM would be a regression.
        /// </summary>
        [Test]
        public void Does_Not_Implement_IAllowsPlayerMovement()
        {
            Assert.IsFalse(
                typeof(IAllowsPlayerMovement).IsAssignableFrom(typeof(FSMRuntimeEditor)),
                "FSM is a data-catalog editor and must freeze player input on open " +
                "— matches the convention used by Particles/Spells/Entities/Map.");
        }

        [Test]
        public void EditorName_Returns_FSMEditorString()
        {
            var ed = CreateEditor();

            Assert.AreEqual("FSM Editor", ed.EditorName,
                "EditorName must match the canonical display string used by the ESC launcher.");
        }

        [Test]
        public void IsActive_InitiallyFalse()
        {
            var ed = CreateEditor();

            Assert.IsFalse(ed.IsActive, "Editor must start inactive.");
        }

        // ── Hotkey wiring ────────────────────────────────────────────────────────

        [Test]
        public void Hotkey_ToggleFSM_FallbackPath_Is_F12()
        {
            // FallbackPath is the canonical InputSystem binding string used when the
            // generated ValkurInputActions asset hasn't been loaded yet (EditMode tests,
            // boot race). Pinning it to f12 prevents an accidental rebind regression.
            var path = EditorHotkeyBindings.FallbackPath(EditorHotkeyBindings.Hotkey.ToggleFSM);
            Assert.AreEqual("<Keyboard>/f12", path,
                "FSM Editor hotkey must remain bound to F12.");
        }

        // ── Activate / Deactivate ────────────────────────────────────────────────

        [Test]
        public void Activate_BuildsUIWithoutThrowing()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);

            Assert.DoesNotThrow(() => ed.Activate(),
                "Activate() must not throw — LoadSets/RefreshSetsList must be robust to empty StreamingAssets/FSM.");

            Assert.IsTrue(ed.IsActive, "IsActive must be true after Activate().");
            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must exist after Activate.");
            Assert.IsTrue(root.activeSelf, "_root must be visible after Activate.");
        }

        [Test]
        public void Deactivate_Sets_IsActive_False_And_Hides_Root()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);
            ed.Activate();

            Assert.DoesNotThrow(() => ed.Deactivate());

            Assert.IsFalse(ed.IsActive, "IsActive must be false after Deactivate.");
            var root = GetField(ed, "_root") as GameObject;
            Assert.IsTrue(root != null, "_root must still exist after Deactivate (UI is reused).");
            Assert.IsFalse(root.activeSelf, "_root must be hidden after Deactivate.");
        }

        [Test]
        public void Deactivate_Clears_Selection()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);
            ed.Activate();

            // Seed a fake selection so we can verify it is cleared.
            SetField(ed, "_selectedSet",        new FSMRuntimeEditor.FSMSetData       { id = "set_x" });
            SetField(ed, "_selectedState",      new FSMRuntimeEditor.FSMStateNode     { id = "node_x" });
            SetField(ed, "_selectedTransition", new FSMRuntimeEditor.FSMTransitionData{ id = "tr_x" });

            ed.Deactivate();

            Assert.IsNull(GetField(ed, "_selectedSet"),        "_selectedSet must be cleared on Deactivate.");
            Assert.IsNull(GetField(ed, "_selectedState"),      "_selectedState must be cleared on Deactivate.");
            Assert.IsNull(GetField(ed, "_selectedTransition"), "_selectedTransition must be cleared on Deactivate.");
        }

        [Test]
        public void ToggleActive_Flips_IsActive_Twice()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);

            Invoke(ed, "ToggleActive");
            Assert.IsTrue(ed.IsActive, "First toggle must activate.");

            Invoke(ed, "ToggleActive");
            Assert.IsFalse(ed.IsActive, "Second toggle must deactivate.");
        }

        [Test]
        public void ActivateTwice_DoesNotRecreateRoot()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);
            ed.Activate();
            var firstRoot = GetField(ed, "_root") as GameObject;

            ed.Deactivate();
            ed.Activate();

            var secondRoot = GetField(ed, "_root") as GameObject;
            Assert.AreSame(firstRoot, secondRoot,
                "_root must be reused on subsequent Activate calls (no rebuild on toggle).");
            Assert.IsTrue(secondRoot.activeSelf, "_root must be active again.");
        }

        // ── Tutorial overlay ─────────────────────────────────────────────────────

        [Test]
        public void TutorialOverlay_StartsHidden_AfterBuildUI()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);

            var tut = GetField(ed, "_tutorial") as GameObject;
            Assert.IsNotNull(tut, "Tutorial overlay must be built by Start().");
            Assert.IsFalse(tut.activeSelf, "Tutorial must start hidden.");
        }

        [Test]
        public void ToggleTutorial_FlipsActiveState()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);
            var tut = GetField(ed, "_tutorial") as GameObject;

            Assert.IsFalse(tut.activeSelf, "Tutorial starts hidden.");
            Invoke(ed, "ToggleTutorial");
            Assert.IsTrue(tut.activeSelf, "First toggle must show tutorial.");
            Invoke(ed, "ToggleTutorial");
            Assert.IsFalse(tut.activeSelf, "Second toggle must hide tutorial.");
        }

        // ── UI panel chrome ──────────────────────────────────────────────────────

        [Test]
        public void BuildUI_Creates_AllExpectedPanels()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);

            Assert.IsNotNull(GetField(ed, "_setsContent"),  "_setsContent must be wired by FSMEditorUIBuilder.");
            Assert.IsNotNull(GetField(ed, "_graphArea"),    "_graphArea must be wired.");
            Assert.IsNotNull(GetField(ed, "_graphContent"), "_graphContent must be wired.");
            Assert.IsNotNull(GetField(ed, "_propsTmp"),     "_propsTmp (properties text) must be wired.");
            Assert.IsNotNull(GetField(ed, "_statusTmp"),    "_statusTmp (status text) must be wired.");
            Assert.IsNotNull(GetField(ed, "_searchBox"),    "_searchBox must be wired.");
        }

        // ── Persistence robustness ───────────────────────────────────────────────

        /// <summary>
        /// Activate() invokes LoadSets() which reads five JSON files from
        /// StreamingAssets/FSM. The persistence layer must tolerate every file
        /// being missing — otherwise a fresh checkout (or a fresh user) would
        /// crash the editor on first open.
        /// </summary>
        [Test]
        public void Activate_DoesNotThrow_WhenStreamingAssetsAreEmpty()
        {
            LogAssert.ignoreFailingMessages = true;
            var ed = CreateEditor(buildUI: true);

            Assert.DoesNotThrow(() => ed.Activate(),
                "FSM editor must open cleanly even when no JSON files exist on disk.");

            // Sets list backing field must always be a non-null, possibly-empty list.
            var sets = GetField(ed, "_fsmSets") as IList<FSMRuntimeEditor.FSMSetData>;
            Assert.IsNotNull(sets, "_fsmSets must never be null after LoadSets — empty list is fine.");
        }
    }
}
