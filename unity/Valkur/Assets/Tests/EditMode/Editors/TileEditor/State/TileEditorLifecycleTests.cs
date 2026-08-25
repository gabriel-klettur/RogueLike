using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Core;
using Valkur.Gameplay.TileEditor;
using static Valkur.Gameplay.TileEditor.TileEditorUIHelpers;

namespace Valkur.Tests.EditMode.Editors.TileEditor.State
{
    /// <summary>
    /// Regression coverage for two <see cref="TileEditorManager"/> lifecycle bugs:
    ///
    /// BUG 3 — <c>HandleToggle</c>'s activate branch reset
    /// <see cref="TileEditorState.CurrentColliderMode"/> to None but never reset
    /// <see cref="TileEditorState.CurrentLayerJumpMode"/>. Closing the editor
    /// while "Draw Layer Jumps" was active and reopening it left the mouse still
    /// painting layer-jump triggers even though the toolbar showed no mode
    /// selected (Select tool, no toggle highlighted). The fix adds the same
    /// reset + <c>_ui.RefreshLayerJumpsToggles()</c> repaint alongside the
    /// existing collider-mode reset (<c>TileEditorManager.InputHandlers.cs</c>).
    ///
    /// BUG 4 — <see cref="TileEditorManager"/> never unregistered itself from
    /// <see cref="GameEditorManager"/> in <c>OnDestroy</c>, unlike all 14 other
    /// <see cref="GameEditorManager.IGameEditor"/> implementations (Boss,
    /// Buildings, Camera, DungeonNodeGraph, Entities, FSM, General, Inventory,
    /// Items, Lighting, Particles, Spawners, Spells, TimeWeather). A destroyed
    /// Tile Editor instance lingered in the manager's registry.
    ///
    /// No dedicated cross-editor "IGameEditor parity" test file iterating all 14
    /// implementations exists in the suite today — each editor's OnDestroy
    /// contract is instead covered by its own lifecycle test file (see
    /// ParticlesEditorLifecycleTests, FSMRuntimeEditorLifecycleTests). This file
    /// follows that same convention for the Tile Editor.
    /// </summary>
    [TestFixture]
    public class TileEditorLifecycleTests
    {
        private readonly List<GameObject> _scene = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _scene)
                if (go != null) Object.DestroyImmediate(go);
            _scene.Clear();

            SetSingletonInstance<TileEditorManager>(null);
            SetSingletonInstance<GameEditorManager>(null);
            LogAssert.ignoreFailingMessages = false;
        }

        // ── Reflection helpers (mirrors the pattern used across the TileEditor
        //    test suite — see ClipboardOutlineTests / ParticlesEditorLifecycleTests) ──

        private static void SetSingletonInstance<T>(T value) where T : MonoBehaviour
        {
            var baseType = typeof(T).BaseType; // SingletonMonoBehaviour<T>
            var f = baseType?.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            f?.SetValue(null, value);
        }

        private static FieldInfo FindField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        private static T GetField<T>(object obj, string name)
        {
            var f = FindField(obj, name);
            Assert.IsNotNull(f, $"Reflection: field '{name}' not found on {obj.GetType().Name}.");
            return (T)f.GetValue(obj);
        }

        private static void SetField(object obj, string name, object value)
        {
            var f = FindField(obj, name);
            Assert.IsNotNull(f, $"Reflection: field '{name}' not found on {obj.GetType().Name}.");
            f.SetValue(obj, value);
        }

        private static void InvokeMethod(object obj, string methodName)
        {
            var t = obj.GetType();
            MethodInfo m = null;
            while (t != null && m == null)
            {
                m = t.GetMethod(methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                t = t.BaseType;
            }
            Assert.IsNotNull(m, $"Reflection: method '{methodName}' not found.");
            m.Invoke(obj, null);
        }

        /// <summary>
        /// Builds a <see cref="TileEditorManager"/> wired to a fully-built
        /// <see cref="TileEditorUI"/> (real <c>UIRefs</c>, via <c>Initialize</c>) plus a
        /// fresh <see cref="TileEditorUndoSystem"/> — the minimum <c>HandleToggle</c>
        /// needs to run both its activate and deactivate branches without a
        /// <c>WorldGridBuilder</c> / <c>ZoneManager</c> / catalog in the scene
        /// (mirrors the "no full Start()" approach used by
        /// ClipboardOutlineTests.AttachWorldGrid — but here we don't even need a
        /// grid, since neither branch under test touches a tilemap).
        /// </summary>
        private (TileEditorManager manager, TileEditorUI ui) NewManagerWithUI()
        {
            SetSingletonInstance<TileEditorManager>(null);
            // Defensive: Deactivate() reaches GameEditorManager.Instance if one
            // happens to be lingering from another fixture in the same domain
            // (Domain Reload is off). NotifyDeactivated is a harmless no-op
            // against an unrelated manager, but starting from a known "no
            // instance" state keeps this fixture's assertions independent of
            // test execution order.
            SetSingletonInstance<GameEditorManager>(null);

            var managerGo = new GameObject("TileEditorManager_LifecycleTest");
            _scene.Add(managerGo);
            var manager = managerGo.AddComponent<TileEditorManager>();

            var uiGo = new GameObject("TileEditorUI_LifecycleTest");
            uiGo.transform.SetParent(managerGo.transform);
            _scene.Add(uiGo);
            var ui = uiGo.AddComponent<TileEditorUI>();
            ui.Initialize(manager.State, catalog: null,
                onTileSelected: null, onToolChanged: null,
                onLayerChanged: null, onBrushSizeChanged: null);

            SetField(manager, "_ui", ui);
            SetField(manager, "_undo", new TileEditorUndoSystem());

            return (manager, ui);
        }

        // ════════════════════════════════════════════════════════════════════
        // BUG 3 — CurrentLayerJumpMode reset on reopen
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Reopen_ResetsCurrentLayerJumpMode_ToNone()
        {
            LogAssert.ignoreFailingMessages = true;
            var (manager, _) = NewManagerWithUI();

            manager.Activate();
            // Simulate the user having switched into "Draw Layer Jumps" mid-session
            // (as clicking the Jumps panel's Draw toggle would).
            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Draw;

            // Close and reopen — the exact F8/F8 repro from the bug report.
            manager.Deactivate();
            manager.Activate();

            Assert.AreEqual(TileEditorState.LayerJumpMode.None, manager.State.CurrentLayerJumpMode,
                "Reopening the Tile Editor must reset CurrentLayerJumpMode to None. Before the fix " +
                "only CurrentColliderMode was reset here, leaving the mouse painting layer-jump " +
                "triggers on every subsequent click even though the toolbar showed Select/no mode.");

            manager.Deactivate();
        }

        [Test]
        public void Reopen_StillResets_CurrentColliderMode_ToNone()
        {
            // Regression guard for the sibling reset this fix sits next to — must
            // keep working exactly as before the LayerJumpMode fix was added.
            LogAssert.ignoreFailingMessages = true;
            var (manager, _) = NewManagerWithUI();

            manager.Activate();
            manager.State.CurrentColliderMode = TileEditorState.ColliderMode.Draw;

            manager.Deactivate();
            manager.Activate();

            Assert.AreEqual(TileEditorState.ColliderMode.None, manager.State.CurrentColliderMode,
                "CurrentColliderMode must still reset to None on reopen — unaffected by the " +
                "LayerJumpMode fix added alongside it.");

            manager.Deactivate();
        }

        [Test]
        public void Reopen_RepaintsDrawLayerJumpsToggle_BackToOffColor()
        {
            // Proves the fix repaints the panel, not just the underlying flag —
            // the user-visible symptom was the Jumps panel's Draw toggle staying
            // lit (and the mouse still painting) after a close/reopen cycle.
            LogAssert.ignoreFailingMessages = true;
            var (manager, ui) = NewManagerWithUI();
            var refs = GetField<TileEditorUIBuilder.UIRefs>(ui, "_refs");
            Assert.IsNotNull(refs.DrawLayerJumpsToggleImg,
                "Sanity: BuildAll must populate DrawLayerJumpsToggleImg.");

            manager.Activate();
            manager.State.CurrentLayerJumpMode = TileEditorState.LayerJumpMode.Draw;
            ui.RefreshLayerJumpsToggles();
            var onColor = new Color(COLLIDER_BORDER.r, COLLIDER_BORDER.g, COLLIDER_BORDER.b, 0.30f);
            Assert.AreEqual(onColor, refs.DrawLayerJumpsToggleImg.color,
                "Sanity: the toggle must actually show the ON tint before we test that reopening clears it.");

            manager.Deactivate();
            manager.Activate();

            Assert.AreEqual(BTN_NORMAL, refs.DrawLayerJumpsToggleImg.color,
                "Reopening must call RefreshLayerJumpsToggles() so the Draw toggle returns to its " +
                "OFF colour — before the fix the flag reset silently (if at all) with no repaint, " +
                "so the panel kept showing Draw as active.");

            manager.Deactivate();
        }

        // ════════════════════════════════════════════════════════════════════
        // BUG 4 — OnDestroy unregisters from GameEditorManager
        // ════════════════════════════════════════════════════════════════════

        [Test]
        public void Implements_IGameEditor_Interface()
        {
            Assert.IsTrue(
                typeof(GameEditorManager.IGameEditor).IsAssignableFrom(typeof(TileEditorManager)),
                "TileEditorManager must implement GameEditorManager.IGameEditor.");
        }

        [Test]
        public void OnDestroy_UnregistersFromGameEditorManager()
        {
            LogAssert.ignoreFailingMessages = true;
            SetSingletonInstance<GameEditorManager>(null);
            SetSingletonInstance<TileEditorManager>(null);

            var mgrGo = new GameObject("GameEditorManager_LifecycleTest");
            _scene.Add(mgrGo);
            var mgr = mgrGo.AddComponent<GameEditorManager>();
            // AddComponent does not reliably pump Awake in EditMode — force the
            // singleton field directly so GameEditorManager.HasInstance/.Instance
            // resolve to THIS manager (matches GeneralEditorManagerTests pattern).
            SetSingletonInstance(mgr);

            var edGo = new GameObject("TileEditorManager_LifecycleTest_Destroy");
            _scene.Add(edGo);
            var editor = edGo.AddComponent<TileEditorManager>();

            mgr.Register(editor);
            var registered = GetField<List<GameEditorManager.IGameEditor>>(mgr, "_registered");
            Assert.IsTrue(registered.Contains(editor),
                "Sanity: Register must add the editor to the manager's internal list.");

            // EditMode doesn't reliably fire OnDestroy via DestroyImmediate — invoke
            // the lifecycle method directly, mirroring
            // PanelChromeTests.OnDestroy_RemovesFromRegistry.
            InvokeMethod(editor, "OnDestroy");

            Assert.IsFalse(registered.Contains(editor),
                "TileEditorManager.OnDestroy must call GameEditorManager.Instance.Unregister(this) " +
                "so a destroyed editor doesn't linger in the registry — matches the other 14 " +
                "IGameEditor implementations (Boss, Buildings, Camera, DungeonNodeGraph, Entities, " +
                "FSM, General, Inventory, Items, Lighting, Particles, Spawners, Spells, TimeWeather).");
        }

        [Test]
        public void OnDestroy_WhenNoGameEditorManagerExists_DoesNotThrow()
        {
            // The fix guards with `if (GameEditorManager.HasInstance)` — a scene
            // that never spun one up (e.g. a narrow unit-test scene) must not
            // crash on teardown.
            LogAssert.ignoreFailingMessages = true;
            SetSingletonInstance<GameEditorManager>(null);
            SetSingletonInstance<TileEditorManager>(null);

            var edGo = new GameObject("TileEditorManager_NoManager");
            _scene.Add(edGo);
            var editor = edGo.AddComponent<TileEditorManager>();

            Assert.DoesNotThrow(() => InvokeMethod(editor, "OnDestroy"),
                "OnDestroy must tolerate a scene with no GameEditorManager instance.");
        }
    }
}
