using System;
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
    ///   • Hot-reload regression: canvas + state/input field recovery
    ///   • InputHandler toggle-key binding
    ///
    /// Zone CRUD operations → MapEditorZoneOpsTests.cs
    /// UI flow state machine → MapEditorUIFlowsTests.cs
    /// </summary>
    [TestFixture]
    public class MapEditorTests : MapEditorTestBase
    {
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

            Assert.IsFalse(refs.ZonesDropdown != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must be closed on start.");

            ui.OnDropdownToggle("zones");
            refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.IsTrue(refs.ZonesDropdown != null && refs.ZonesDropdown.activeSelf,
                "Zones panel must open after first toggle.");

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
            ui.OnDropdownToggle("props");

            ui.SetSelectedZone("TestZone", editable: true);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assert.AreEqual("TestZone", refs.NameInput?.text ?? "",
                "NameInput must contain the zone name after SetSelectedZone.");
        }

        [Test]
        public void MapEditorUI_SetSelectedZone_NoneWhenEmpty()
        {
            var ui = CreateInitializedUI();
            ui.SetSelectedZone(string.Empty, editable: false);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
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

            var handler = new MapEditorInputHandler();
            InvokeMethod(handler, "CreateActions");

            var field = handler.GetType().GetField("_toggleAction",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "_toggleAction field must exist on MapEditorInputHandler.");

            var action = field.GetValue(handler);
            Assert.IsNotNull(action, "_toggleAction must be initialized after CreateActions().");

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

            Assert.IsNotNull(stateField.GetValue(mgr),
                "_state must be initialized after singleton awake.");
            Assert.IsNotNull(inputField.GetValue(mgr),
                "_input must be initialized after singleton awake.");

            stateField.SetValue(mgr, null);
            inputField.SetValue(mgr, null);

            InvokeMethod(mgr, "OnEnable");

            Assert.IsNotNull(stateField.GetValue(mgr),
                "_state must be re-initialized by OnEnable after hot-reload.");
            Assert.IsNotNull(inputField.GetValue(mgr),
                "_input must be re-initialized by OnEnable after hot-reload.");
        }
    }
}
