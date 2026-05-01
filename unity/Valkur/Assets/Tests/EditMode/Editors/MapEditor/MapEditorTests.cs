using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using Valkur.Gameplay.MapEditor;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// EditMode tests for the Map Editor (F11) UI/UX overhaul.
    ///
    /// Scope:
    ///   • MapEditorManager: EditorName, IsActive, camera-pan fields present
    ///   • MapEditorUIBuilder: ApplyMenuBtnStyle colour/font transitions
    ///   • MapEditorUI: Initialize, SetVisible (canvas.enabled), OnDropdownToggle,
    ///     SetSelectedZone, SetRestrictToggle, SetStatus,
    ///     ShowAddZoneDialog/HideAddZoneDialog, ShowDeleteZoneDialog/HideDeleteZoneDialog
    ///   • RebuildZonesList smoke-test (empty zone set → no child objects)
    ///
    /// Pattern mirrors BuildingsEditorLifecycleTests.cs — reflection helpers,
    /// scene-object teardown, LogAssert suppression for renderer-material warnings.
    /// </summary>
    [TestFixture]
    public class MapEditorTests
    {
        private readonly List<GameObject>     _sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _assets     = new List<ScriptableObject>();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void ClearSingletonInstance<T>() where T : MonoBehaviour
        {
            var type = typeof(T).BaseType;
            while (type != null)
            {
                var field = type.GetField("_instance",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field != null) { field.SetValue(null, null); return; }
                type = type.BaseType;
            }
        }

        private T CreateSingleton<T>(string name = "TestGO") where T : MonoBehaviour
        {
            ClearSingletonInstance<T>();
            var go   = new GameObject(name);
            var comp = go.AddComponent<T>();
            InvokeMethod(comp, "OnSingletonAwake");
            _sceneObjects.Add(go);
            return comp;
        }

        private static FieldInfo GetField(object obj, string name)
        {
            var t = obj.GetType();
            while (t != null)
            {
                var f = t.GetField(name,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

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

        private static void SetField(object obj, string name, object value)
            => GetField(obj, name)?.SetValue(obj, value);

        private static object GetFieldValue(object obj, string name)
            => GetField(obj, name)?.GetValue(obj);

        /// <summary>
        /// Creates a minimal MapEditorUI (MonoBehaviour partial class) and calls
        /// Initialize() with no-op callbacks so BuildUI() runs inside EditMode.
        /// </summary>
        private MapEditorUI CreateInitializedUI()
        {
            var go = new GameObject("MapEditorUI");
            _sceneObjects.Add(go);
            var ui = go.AddComponent<MapEditorUI>();

            var state = new MapEditorState();
            LogAssert.ignoreFailingMessages = true;
            ui.Initialize(
                state,
                _ => { },           // onZoneSelected
                () => { },          // onBeginAddZoneFlow
                (n, t, e) => { },   // onConfirmAddZone
                () => { },          // onCancelAddZoneFlow
                () => { },          // onDuplicateSelectedZone
                () => { },          // onRequestDeleteSelectedZone
                () => { },          // onConfirmDeleteSelectedZone
                _ => { },           // onRenameSelectedZone
                (o, n) => { },      // onRenameZoneByName
                () => { },          // onToggleSelectedZoneEditable
                _ => { },           // onToggleZoneEditableByName
                _ => { });          // onRestrictEditChanged

            return ui;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _sceneObjects)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _sceneObjects.Clear();

            foreach (var so in _assets)
                if (so != null) UnityEngine.Object.DestroyImmediate(so);
            _assets.Clear();

            LogAssert.ignoreFailingMessages = false;
        }

        // ── MapEditorManager: basic contract ─────────────────────────────────────

        [Test]
        public void MapEditorManager_EditorName_IsCorrect()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("TestMapEditorManager");
            Assert.AreEqual("Map Editor", mgr.EditorName,
                "EditorName must match the canonical 'Map Editor' identifier.");
        }

        [Test]
        public void MapEditorManager_IsActive_FalseByDefault()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("TestMapEditorManager");
            Assert.IsFalse(mgr.IsActive,
                "IsActive must be false before Activate() is called.");
        }

        [Test]
        public void MapEditorManager_UsesSharedCameraPanController()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("TestMapEditorManager");

            // After the editor-camera-pan refactor, every runtime editor
            // (Tile / Buildings / Map / Entities / Items / Inventory / Spells /
            // Lighting / Particles) holds an EditorCameraPanController instance
            // instead of duplicating _isPanning / _panAnchor* state inline.
            var panField = GetField(mgr, "_cameraPan");
            Assert.IsNotNull(panField,
                "_cameraPan field required (shared EditorCameraPanController).");
            Assert.AreEqual(typeof(Valkur.Gameplay.Editors.EditorCameraPanController),
                panField.FieldType,
                "_cameraPan must be of type EditorCameraPanController.");
            Assert.IsNotNull(panField.GetValue(mgr),
                "_cameraPan should be initialised by the field initializer.");
        }

        // ── MapEditorUIBuilder: ApplyMenuBtnStyle ─────────────────────────────────

        [Test]
        public void ApplyMenuBtnStyle_OpenState_HighlightsButton()
        {
            LogAssert.ignoreFailingMessages = true;

            // Use an already-initialized UI so Image and TMP are in a valid canvas
            // context — creating them bare in EditMode leaves CanvasUpdateRegistry
            // with stale dead references from prior teardowns which causes NRE.
            var ui   = CreateInitializedUI();
            var refs = (MapEditorUIBuilder.UIRefs)GetFieldValue(ui, "_refs");
            var img  = refs.ZonesMenuBtnImg;
            var tmp  = refs.ZonesMenuBtnTmp;

            Assume.That(img != null, "UIRefs.ZonesMenuBtnImg must be wired after Initialize.");
            Assume.That(tmp != null, "UIRefs.ZonesMenuBtnTmp must be wired after Initialize.");

            MapEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: true);

            Assert.AreNotEqual(img.color, new Color(0f, 0f, 0f, 0f),
                "Open state must set a non-default colour on the Image.");
            Assert.AreEqual(FontStyles.Bold, tmp.fontStyle,
                "Open button must use Bold text to indicate active panel.");
        }

        [Test]
        public void ApplyMenuBtnStyle_ClosedState_NormalButton()
        {
            LogAssert.ignoreFailingMessages = true;

            // Use an already-initialized UI so Image and TMP are in a valid canvas context.
            var ui   = CreateInitializedUI();
            var refs = (MapEditorUIBuilder.UIRefs)GetFieldValue(ui, "_refs");
            var img  = refs.ZonesMenuBtnImg;
            var tmp  = refs.ZonesMenuBtnTmp;

            Assume.That(img != null, "UIRefs.ZonesMenuBtnImg must be wired after Initialize.");
            Assume.That(tmp != null, "UIRefs.ZonesMenuBtnTmp must be wired after Initialize.");

            // Set to open first, then close
            MapEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: true);
            var openColour = img.color;

            MapEditorUIBuilder.ApplyMenuBtnStyle(img, tmp, isOpen: false);

            Assert.AreNotEqual(openColour, img.color,
                "Closed state must produce a different colour than open state.");
            Assert.AreEqual(FontStyles.Normal, tmp.fontStyle,
                "Closed button must use Normal text style.");
        }

        [Test]
        public void ApplyMenuBtnStyle_NullSafe_DoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
                MapEditorUIBuilder.ApplyMenuBtnStyle(null, null, isOpen: true),
                "ApplyMenuBtnStyle must not throw when called with null arguments.");
        }

        // ── MapEditorUI: Initialize + SetVisible ─────────────────────────────────

        [Test]
        public void MapEditorUI_Initialize_CanvasStartsHidden()
        {
            var ui = CreateInitializedUI();

            // Prefer GetComponentInChildren over private-field reflection: avoids
            // Unity serialization writing a stale destroyed-transform into [SerializeField]
            // _canvasRoot from a prior test, which causes MissingReferenceException.
            var canvas = ui.GetComponentInChildren<Canvas>(includeInactive: true);
            Assert.IsNotNull(canvas, "A Canvas must exist in MapEditorUI children after Initialize().");
            Assert.IsFalse(canvas.enabled,
                "Canvas must start hidden (SetVisible(false) called in Initialize).");
        }

        [Test]
        public void MapEditorUI_SetVisible_True_EnablesCanvas()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);

            var canvas = ui.GetComponentInChildren<Canvas>(includeInactive: true);
            Assert.IsNotNull(canvas);
            Assert.IsTrue(canvas.enabled,
                "Canvas must be enabled after SetVisible(true).");
        }

        [Test]
        public void MapEditorUI_SetVisible_False_DisablesCanvas()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);
            ui.SetVisible(false);

            var canvas = ui.GetComponentInChildren<Canvas>(includeInactive: true);
            Assert.IsNotNull(canvas);
            Assert.IsFalse(canvas.enabled,
                "Canvas must be disabled after SetVisible(false).");
        }

        // ── MapEditorUI: OnDropdownToggle ─────────────────────────────────────────

        [Test]
        public void MapEditorUI_OnDropdownToggle_OpensThenCloses()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");

            // Initially the panel is closed
            Assert.IsFalse(refs.ZonesDropdown != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must be closed on start.");

            // First toggle → open
            ui.OnDropdownToggle("zones");
            refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.ZonesDropdown != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must open after first toggle.");

            // Second toggle → close
            ui.OnDropdownToggle("zones");
            refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsFalse(refs.ZonesDropdown != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must close after second toggle.");
        }

        [Test]
        public void MapEditorUI_OnDropdownToggle_PanelsAreIndependent()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);

            ui.OnDropdownToggle("zones");
            ui.OnDropdownToggle("actions");

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.ZonesDropdown   != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must stay open when Actions panel opens.");
            Assert.IsTrue(refs.ActionsDropdown != null && refs.ActionsDropdown.activeSelf,
                "Actions panel must open independently.");
        }

        [Test]
        public void MapEditorUI_SetVisible_False_ClosesAllDropdowns()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);
            ui.OnDropdownToggle("zones");
            ui.OnDropdownToggle("actions");
            ui.SetVisible(false);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsFalse(refs.ZonesDropdown   != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must be closed when editor is hidden.");
            Assert.IsFalse(refs.ActionsDropdown != null && refs.ActionsDropdown.activeSelf,
                "Actions panel must be closed when editor is hidden.");
        }

        // ── MapEditorUI: SetSelectedZone ──────────────────────────────────────────

        [Test]
        public void MapEditorUI_SetSelectedZone_UpdatesTexts()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);
            ui.OnDropdownToggle("props");   // open Properties panel so refs are active

            ui.SetSelectedZone("TestZone", editable: true);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            // Zone name must be pre-filled in the Properties panel rename input.
            Assert.AreEqual("TestZone", refs.NameInput?.text ?? "",
                "NameInput must contain the zone name after SetSelectedZone.");
        }

        [Test]
        public void MapEditorUI_SetSelectedZone_NoneWhenEmpty()
        {
            var ui = CreateInitializedUI();
            ui.SetSelectedZone(string.Empty, editable: false);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            // For an empty zone name, NameInput must not be changed (stays empty / untouched).
            Assert.IsFalse((refs.NameInput?.text ?? "").Contains("TestZone"),
                "NameInput must not contain a stale zone name when zone is cleared.");
        }

        // ── MapEditorUI: SetRestrictToggle + SetStatus ─────────────────────────────

        [Test]
        public void MapEditorUI_SetRestrictToggle_SetsValue()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);
            ui.OnDropdownToggle("props");

            ui.SetRestrictToggle(true);
            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.RestrictToggle == null || refs.RestrictToggle.isOn,
                "RestrictToggle must be on after SetRestrictToggle(true).");
        }

        [Test]
        public void MapEditorUI_SetStatus_UpdatesStatusText()
        {
            const string msg = "Zone 'Alpha' selected.";
            var ui = CreateInitializedUI();
            ui.SetStatus(msg);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.AreEqual(msg, refs.StatusBarText?.text,
                "StatusBarText must reflect the message passed to SetStatus().");
        }

        // ── MapEditorUI: AddZone dialog ───────────────────────────────────────────

        [Test]
        public void MapEditorUI_ShowAddZoneDialog_DialogBecomesVisible()
        {
            var ui = CreateInitializedUI();
            ui.ShowAddZoneDialog("NewZone", "SourceZone", sourceEditable: false);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.AddZoneDialog == null || refs.AddZoneDialog.activeSelf,
                "AddZoneDialog must be active after ShowAddZoneDialog().");
            Assert.IsTrue(ui.IsModalOpen,
                "IsModalOpen must return true while add dialog is visible.");
        }

        [Test]
        public void MapEditorUI_HideAddZoneDialog_DialogBecomesHidden()
        {
            var ui = CreateInitializedUI();
            ui.ShowAddZoneDialog("NewZone", "SourceZone", sourceEditable: true);
            ui.HideAddZoneDialog();

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsFalse(refs.AddZoneDialog != null && refs.AddZoneDialog.activeSelf,
                "AddZoneDialog must be inactive after HideAddZoneDialog().");
            Assert.IsFalse(ui.IsModalOpen,
                "IsModalOpen must return false after HideAddZoneDialog().");
        }

        // ── MapEditorUI: DeleteZone dialog ────────────────────────────────────────

        [Test]
        public void MapEditorUI_ShowDeleteZoneDialog_PromptContainsZoneName()
        {
            var ui = CreateInitializedUI();
            ui.ShowDeleteZoneDialog("Alpha");

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.DeleteZoneDialog == null || refs.DeleteZoneDialog.activeSelf,
                "DeleteZoneDialog must be active after ShowDeleteZoneDialog().");
            StringAssert.Contains("Alpha", refs.DeleteZonePrompt?.text ?? "",
                "DeleteZonePrompt must contain the zone name.");
        }

        [Test]
        public void MapEditorUI_HideDeleteZoneDialog_DialogBecomesHidden()
        {
            var ui = CreateInitializedUI();
            ui.ShowDeleteZoneDialog("Beta");
            ui.HideDeleteZoneDialog();

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsFalse(refs.DeleteZoneDialog != null && refs.DeleteZoneDialog.activeSelf,
                "DeleteZoneDialog must be inactive after HideDeleteZoneDialog().");
        }

        // ── MapEditorUI: RefreshZones (empty set) ─────────────────────────────────

        [Test]
        public void MapEditorUI_RefreshZones_EmptyArray_NoChildObjects()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);
            ui.OnDropdownToggle("zones");

            ui.RefreshZones(Array.Empty<ZoneManager.ZoneDefinition>());

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            int childCount = refs.ZonesListContent != null
                ? refs.ZonesListContent.childCount
                : 0;

            Assert.AreEqual(0, childCount,
                "ZonesListContent must have no children after RefreshZones with empty array.");
        }

        // ── MapEditorInputHandler: toggle key ─────────────────────────────────────

        [Test]
        public void MapEditorInputHandler_ToggleKey_IsF11()
        {
            LogAssert.ignoreFailingMessages = true;

            // Verify the F11 binding string in the action map
            var handler = new MapEditorInputHandler();
            InvokeMethod(handler, "CreateActions");

            var field = handler.GetType().GetField("_toggleAction",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_toggleAction field must exist on MapEditorInputHandler.");

            var action = field.GetValue(handler);
            Assert.IsNotNull(action, "_toggleAction must be initialized after CreateActions().");

            // Verify the binding path contains "f11" (Unity Input System uses lowercase
            // key names, e.g. "<Keyboard>/f11"). Use case-insensitive comparison.
            var bindings = action.GetType().GetProperty("bindings")?.GetValue(action);
            bool hasF11 = false;
            if (bindings is System.Collections.IEnumerable bindingsList)
            {
                foreach (var binding in bindingsList)
                {
                    var pathProp = binding.GetType().GetProperty("path");
                    var path = pathProp?.GetValue(binding)?.ToString() ?? "";
                    if (path.IndexOf("f11", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasF11 = true;
                        break;
                    }
                }
            }
            Assert.IsTrue(hasF11, "MapEditorInputHandler toggle action must bind to F11.");
        }

        // ── Regression: hot-reload defensive recovery ────────────────────────────

        /// <summary>
        /// Bug repro: after a hot-reload, MapEditorUI._canvasRoot was lost
        /// (Unity-null) while the actual MapEditorCanvas GameObject persisted in
        /// the scene. SetVisible(false) silently no-op'd and the canvas stayed
        /// enabled, making the editor "stuck open" on F11 toggle.
        /// </summary>
        [Test]
        public void MapEditorUI_SetVisible_RecoversWhenCanvasFieldLost()
        {
            var ui = CreateInitializedUI();
            ui.SetVisible(true);

            // Simulate hot-reload: null the private _canvasRoot AND _cachedCanvas
            // references while leaving the actual canvas GameObject alive.
            var canvasRootField = typeof(MapEditorUI).GetField("_canvasRoot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedCanvasField = typeof(MapEditorUI).GetField("_cachedCanvas",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(canvasRootField, "_canvasRoot field must exist.");
            Assert.IsNotNull(cachedCanvasField, "_cachedCanvas field must exist.");

            canvasRootField.SetValue(ui, null);
            cachedCanvasField.SetValue(ui, null);

            // Canvas should still be reachable via children and the next
            // SetVisible(false) MUST disable it.
            var canvas = ui.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "Underlying canvas must still exist in scene.");
            Assert.IsTrue(canvas.enabled,
                "Canvas should still be enabled before SetVisible(false).");

            ui.SetVisible(false);

            Assert.IsFalse(canvas.enabled,
                "Canvas must be disabled by SetVisible(false) even when " +
                "_canvasRoot/_cachedCanvas were lost (hot-reload recovery).");
        }

        /// <summary>
        /// Bug repro: after a hot-reload, MapEditorManager._state and _input
        /// (private non-serialized fields) were nulled. F11 toggling silently
        /// did nothing because Update() early-returns on _input == null.
        /// EnsureCoreInitialized() (invoked from OnEnable) must restore them.
        /// </summary>
        [Test]
        public void MapEditorManager_OnEnable_RestoresStateAndInputAfterHotReload()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("TestMapEditorManager");

            var stateField = typeof(MapEditorManager).GetField("_state",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var inputField = typeof(MapEditorManager).GetField("_input",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(stateField, "_state field must exist.");
            Assert.IsNotNull(inputField, "_input field must exist.");

            // Sanity: created normally, fields are populated
            Assert.IsNotNull(stateField.GetValue(mgr),
                "_state must be initialized after singleton awake.");
            Assert.IsNotNull(inputField.GetValue(mgr),
                "_input must be initialized after singleton awake.");

            // Simulate hot-reload nulling private fields
            stateField.SetValue(mgr, null);
            inputField.SetValue(mgr, null);

            // OnEnable is a Unity message; invoke directly to simulate re-enable
            // after domain reload. EnsureCoreInitialized() must restore both.
            InvokeMethod(mgr, "OnEnable");

            Assert.IsNotNull(stateField.GetValue(mgr),
                "_state must be re-initialized by OnEnable after hot-reload.");
            Assert.IsNotNull(inputField.GetValue(mgr),
                "_input must be re-initialized by OnEnable after hot-reload.");
        }

        // ── ACTIONS Operations: end-to-end with real ZoneManager ────────────────
        //
        // These tests wire up a MapEditorManager + a real ZoneManager (no UI, no
        // disk persistence — disk writes go to Application.persistentDataPath
        // which is sandboxed for the test runner). They drive each ACTIONS panel
        // operation through reflection (private methods) and assert on
        // ZoneManager state to prove the full call chain works.

        /// <summary>Build a manager wired to a real ZoneManager pre-seeded with
        /// the supplied zones. Skips Start() so we don't need camera/UI/world.</summary>
        private MapEditorManager CreateManagerWithZones(params (string name, Vector2Int offset, bool editable)[] seeds)
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("OpsTestMapEditorManager");

            var zoneManagerGo = new GameObject("OpsTestZoneManager");
            _sceneObjects.Add(zoneManagerGo);
            var zm = zoneManagerGo.AddComponent<ZoneManager>();

            // Seed zones via public AddZone (also fires OnZonesChanged → harmless).
            foreach (var (name, offset, editable) in seeds)
                Assert.IsTrue(zm.AddZone(name, offset, editable),
                    $"Seed zone '{name}' must be addable.");

            SetField(mgr, "zoneManager", zm);
            return mgr;
        }

        private static ZoneManager GetZM(MapEditorManager mgr)
            => (ZoneManager) GetFieldValue(mgr, "zoneManager");

        private static MapEditorState GetState(MapEditorManager mgr)
            => (MapEditorState) GetFieldValue(mgr, "_state");

        // ── Rename ────────────────────────────────────────────────────────────────

        [Test]
        public void Operation_RenameSelectedZone_RenamesInZoneManager()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RenameSelectedZone", "Gamma");

            Assert.IsFalse(GetZM(mgr).TryGetZone("Alpha", out _),
                "'Alpha' must no longer exist after rename.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Gamma", out _),
                "'Gamma' must exist after rename.");
            Assert.AreEqual("Gamma", GetState(mgr).SelectedZone,
                "Selection must follow the renamed zone.");
        }

        [Test]
        public void Operation_RenameSelectedZone_NoSelection_DoesNotThrow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            Assert.DoesNotThrow(() => InvokeMethod(mgr, "RenameSelectedZone", "Foo"),
                "Rename without a selection must be a safe no-op.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Original zone must remain intact when rename has no selection.");
        }

        [Test]
        public void Operation_RenameSelectedZone_EmptyName_DoesNotRename()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RenameSelectedZone", "   ");

            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Empty rename input must not modify the zone name.");
        }

        // ── Toggle Editable ───────────────────────────────────────────────────────

        [Test]
        public void Operation_ToggleSelectedZoneEditable_FlipsFlag()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "ToggleSelectedZoneEditable");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z1));
            Assert.IsFalse(z1.editableInTileEditor, "Editable flag must flip true→false.");

            InvokeMethod(mgr, "ToggleSelectedZoneEditable");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z2));
            Assert.IsTrue(z2.editableInTileEditor, "Editable flag must flip false→true.");
        }

        // ── Move ──────────────────────────────────────────────────────────────────

        [Test]
        public void Operation_MoveSelectedZone_AppliesZoneStridedDelta()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");
            int w = GetZM(mgr).ZoneWidthTiles;
            int h = GetZM(mgr).ZoneHeightTiles;

            InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.right);
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var moved1));
            Assert.AreEqual(new Vector2Int(w, 0), moved1.gridOffset,
                "Move right must shift by ZoneWidthTiles, not 1.");

            InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.up);
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var moved2));
            Assert.AreEqual(new Vector2Int(w, h), moved2.gridOffset,
                "Move up must add ZoneHeightTiles to Y.");
        }

        [Test]
        public void Operation_MoveSelectedZone_NoSelection_DoesNotThrow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            Assert.DoesNotThrow(() =>
                InvokeMethod(mgr, "MoveSelectedZone", Vector2Int.right));
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out var z));
            Assert.AreEqual(Vector2Int.zero, z.gridOffset,
                "Zone offset must not change when no selection.");
        }

        // ── Duplicate ─────────────────────────────────────────────────────────────

        [Test]
        public void Operation_DuplicateSelectedZone_CreatesShiftedCopy()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            int before = GetZM(mgr).GetZonesSnapshot().Length;
            InvokeMethod(mgr, "DuplicateSelectedZone");
            int after = GetZM(mgr).GetZonesSnapshot().Length;

            Assert.AreEqual(before + 1, after, "Duplicate must add exactly one zone.");
            Assert.AreNotEqual("Alpha", GetState(mgr).SelectedZone,
                "Selection must follow the new duplicate, not the source.");

            Assert.IsTrue(GetZM(mgr).TryGetZone(GetState(mgr).SelectedZone, out var dup));
            Assert.AreEqual(new Vector2Int(GetZM(mgr).ZoneWidthTiles, 0), dup.gridOffset,
                "Duplicate must be shifted right by ZoneWidthTiles to avoid overlap.");
        }

        // ── Delete (request + confirm) ────────────────────────────────────────────

        [Test]
        public void Operation_RequestDelete_StoresPendingDeleteName()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Beta");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            var pending = (string) GetFieldValue(mgr, "_pendingDeleteZoneName");
            Assert.AreEqual("Beta", pending,
                "RequestDelete must stage the selected zone name for confirmation.");
        }

        [Test]
        public void Operation_ConfirmDelete_RemovesPendingZone()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha", Vector2Int.zero,    true),
                ("Beta",  new Vector2Int(50, 0), true));
            GetState(mgr).SelectZone("Beta");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            InvokeMethod(mgr, "ConfirmDeleteSelectedZone");

            Assert.IsFalse(GetZM(mgr).TryGetZone("Beta", out _),
                "'Beta' must be removed after Confirm.");
            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Other zones must remain.");
            Assert.IsNull(GetFieldValue(mgr, "_pendingDeleteZoneName"),
                "_pendingDeleteZoneName must clear after confirm.");
        }

        [Test]
        public void Operation_ConfirmDelete_LastZone_RefusesToDelete()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "RequestDeleteSelectedZone");
            InvokeMethod(mgr, "ConfirmDeleteSelectedZone");

            Assert.IsTrue(GetZM(mgr).TryGetZone("Alpha", out _),
                "Cannot delete the last remaining zone — must refuse.");
        }

        // ── Add Zone Flow ─────────────────────────────────────────────────────────

        [Test]
        public void Operation_BeginAddZoneFlow_NoSelection_StillActivatesFlow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            InvokeMethod(mgr, "BeginAddZoneFlow");

            Assert.IsTrue((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "Add Zone flow must activate even without a pre-selection — source zone is optional.");
        }

        [Test]
        public void Operation_ConfirmAddZone_FromTemplate_AppendsZoneAtTarget()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            // Bypass UI: directly enter the flow + set a target offset
            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);
            SetField(mgr, "_pendingAddZoneOffset", new Vector2Int(50, 0));

            InvokeMethod(mgr, "ConfirmAddZone", "Beta", true, false);

            Assert.IsTrue(GetZM(mgr).TryGetZone("Beta", out var beta),
                "ConfirmAddZone must add the new zone via template path.");
            Assert.AreEqual(new Vector2Int(50, 0), beta.gridOffset);
            Assert.IsFalse(beta.editableInTileEditor,
                "Editable override (false) must be applied to the new zone.");
            Assert.AreEqual("Beta", GetState(mgr).SelectedZone,
                "New zone must become the selection after confirm.");
            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "Flow must end after a successful confirm.");
        }

        [Test]
        public void Operation_ConfirmAddZone_WithoutTarget_DoesNotCreateZone()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", false);

            InvokeMethod(mgr, "ConfirmAddZone", "Beta", true, true);

            Assert.IsFalse(GetZM(mgr).TryGetZone("Beta", out _),
                "ConfirmAddZone must refuse when no target offset has been marked.");
        }

        [Test]
        public void Operation_ConfirmAddZone_EmptyName_DoesNotCreateZone()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);
            SetField(mgr, "_pendingAddZoneOffset", new Vector2Int(50, 0));

            int before = GetZM(mgr).GetZonesSnapshot().Length;
            InvokeMethod(mgr, "ConfirmAddZone", "   ", true, true);
            int after = GetZM(mgr).GetZonesSnapshot().Length;

            Assert.AreEqual(before, after,
                "Empty / whitespace zone name must be rejected by Confirm.");
        }

        [Test]
        public void Operation_CancelAddZoneFlow_ResetsFlowFlags()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget", true);

            InvokeMethod(mgr, "CancelAddZoneFlow");

            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"));
            Assert.IsFalse((bool) GetFieldValue(mgr, "_hasPendingAddTarget"));
        }

        // ── SetRestrictTileEditing ────────────────────────────────────────────────

        [Test]
        public void Operation_SetRestrictTileEditing_PersistsFlag()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            var st  = GetState(mgr);

            InvokeMethod(mgr, "SetRestrictTileEditing", false);
            Assert.IsFalse(st.RestrictTileEditingToEditableZones);

            InvokeMethod(mgr, "SetRestrictTileEditing", true);
            Assert.IsTrue(st.RestrictTileEditingToEditableZones);
        }

        // ── Adaptive overlay-line width ────────────────────────────────────────────

        [Test]
        public void Overlay_ComputeAdaptiveLineWidth_ScalesWithCameraZoom()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));

            // Build a real orthographic camera so ComputeAdaptiveLineWidth has
            // a non-null target.
            var camGo = new GameObject("OpsTestCam");
            _sceneObjects.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            SetField(mgr, "_mainCamera", cam);

            float wClose = (float) typeof(MapEditorManager)
                .GetMethod("ComputeAdaptiveLineWidth",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mgr, null);

            cam.orthographicSize = 50f; // zoom out 10×
            float wFar = (float) typeof(MapEditorManager)
                .GetMethod("ComputeAdaptiveLineWidth",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mgr, null);

            Assert.GreaterOrEqual(wFar, wClose,
                "Adaptive line width must grow (or stay equal at clamp) when zooming out.");
            Assert.GreaterOrEqual(wClose, 0.01f,
                "Adaptive line width must stay strictly positive.");
        }

        // ── Add Zone Mode: blinking button + deferred dialog ──────────────────────

        [Test]
        public void MapEditorUI_AddZoneBtnOutline_NotNull_AfterInitialize()
        {
            var ui   = CreateInitializedUI();
            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsNotNull(refs.AddZoneBtnOutline,
                "UIRefs.AddZoneBtnOutline must be set during BuildAll.");
        }

        [Test]
        public void MapEditorUI_AddZoneBtnImage_NotNull_AfterInitialize()
        {
            var ui   = CreateInitializedUI();
            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsNotNull(refs.AddZoneBtnImage,
                "UIRefs.AddZoneBtnImage must be set during BuildAll.");
        }

        [Test]
        public void MapEditorUI_SetAddZoneMode_True_SetsField()
        {
            var ui = CreateInitializedUI();
            ui.SetAddZoneMode(true);
            Assert.IsTrue((bool) GetFieldValue(ui, "_isAddZoneMode"),
                "SetAddZoneMode(true) must set the _isAddZoneMode field.");
        }

        [Test]
        public void MapEditorUI_SetAddZoneMode_False_ClearsField()
        {
            var ui = CreateInitializedUI();
            ui.SetAddZoneMode(true);
            ui.SetAddZoneMode(false);
            Assert.IsFalse((bool) GetFieldValue(ui, "_isAddZoneMode"),
                "SetAddZoneMode(false) must clear the _isAddZoneMode field.");
        }

        [Test]
        public void MapEditorUI_SetAddZoneMode_False_ResetsOutlineToTransparent()
        {
            var ui   = CreateInitializedUI();
            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            ui.SetAddZoneMode(true);
            ui.SetAddZoneMode(false);
            Assert.AreEqual(0f, refs.AddZoneBtnOutline.effectColor.a,
                "Outline alpha must be reset to 0 when SetAddZoneMode(false) is called.");
        }

        [Test]
        public void Operation_BeginAddZoneFlow_WithSelection_ActivatesFlow()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).SelectZone("Alpha");

            InvokeMethod(mgr, "BeginAddZoneFlow");

            Assert.IsTrue((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "BeginAddZoneFlow must set _isAddZoneFlowActive to true.");
            Assert.IsFalse((bool) GetFieldValue(mgr, "_hasPendingAddTarget"),
                "_hasPendingAddTarget must start false — user has not clicked the map yet.");
        }

        [Test]
        public void Operation_CancelAddZoneFlow_ClearsAllFlowState()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            SetField(mgr, "_isAddZoneFlowActive", true);
            SetField(mgr, "_hasPendingAddTarget",  true);

            InvokeMethod(mgr, "CancelAddZoneFlow");

            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "CancelAddZoneFlow must clear _isAddZoneFlowActive.");
            Assert.IsFalse((bool) GetFieldValue(mgr, "_hasPendingAddTarget"),
                "CancelAddZoneFlow must clear _hasPendingAddTarget.");
        }
    }
}
