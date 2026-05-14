using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Valkur.Gameplay.MapEditor;

namespace Valkur.Tests.EditMode.Editors.MapEditor
{
    /// <summary>
    /// UI state machine flows: Add Zone mode toggles, blinking-button refs,
    /// same-frame race guard, ConfirmAddZone fallback paths,
    /// ShowAddZoneDialog template-toggle rules, GenerateOffsetZoneName,
    /// external overlay sharing, and InputHandler centralization regression.
    /// </summary>
    [TestFixture]
    public class MapEditorUIFlowsTests : MapEditorTestBase
    {
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

        // ── Add Zone flow flags ───────────────────────────────────────────────────

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

        // ── Add Zone same-frame race guard ───────────────────────────────────────
        //
        // BeginAddZoneFlow records Time.frameCount. The Update loop refuses to
        // call MarkAddZoneTargetAtCursor on the same frame the flow started so
        // that the click which activated the flow (over the "Add Zone" UI
        // button) cannot also race ahead and mark a target.

        [Test]
        public void Operation_BeginAddZoneFlow_RecordsCurrentFrameAsStartedFrame()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));

            InvokeMethod(mgr, "BeginAddZoneFlow");

            int started = (int) GetFieldValue(mgr, "_addZoneFlowStartedFrame");
            Assert.AreNotEqual(-1, started,
                "After BeginAddZoneFlow, _addZoneFlowStartedFrame must leave the -1 sentinel " +
                "or the same-frame race guard can never fire.");
            Assert.AreEqual(Time.frameCount, started,
                "_addZoneFlowStartedFrame must equal Time.frameCount so Update can detect the same-frame case.");
        }

        [Test]
        public void MapEditorManager_AddZoneFlowStartedFrame_DefaultsToNegativeOne()
        {
            // Default value must NOT collide with Time.frameCount=0 at boot,
            // otherwise the very first click on Add Zone would race.
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("RaceDefaultMgr");
            int started = (int) GetFieldValue(mgr, "_addZoneFlowStartedFrame");
            Assert.AreEqual(-1, started,
                "_addZoneFlowStartedFrame default must be -1 (sentinel) — never a real frame number.");
        }

        // ── ConfirmAddZone fallback when template ON but no source selected ─────
        //
        // The Add Zone dialog's "Use selected as template" toggle must not be
        // able to silently fail when no source is selected. ConfirmAddZone
        // downgrades useTemplate→false in that case and creates a blank zone.

        [Test]
        public void Operation_ConfirmAddZone_TemplateOnNoSelection_FallsBackToBlankCreate()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));
            GetState(mgr).ClearSelection();

            SetField(mgr, "_isAddZoneFlowActive",  true);
            SetField(mgr, "_hasPendingAddTarget",  true);
            SetField(mgr, "_pendingAddZoneOffset", new Vector2Int(150, 150));

            InvokeMethod(mgr, "ConfirmAddZone", "Beta", true, true);

            Assert.IsTrue(GetZM(mgr).TryGetZone("Beta", out var beta),
                "ConfirmAddZone must fall back to blank create when template is on but no source is selected.");
            Assert.AreEqual(new Vector2Int(150, 150), beta.gridOffset,
                "Fallback path must still honour the pending target offset.");
            Assert.IsTrue(beta.editableInTileEditor,
                "Editable flag from confirm dialog must propagate to the new zone.");
            Assert.IsFalse((bool) GetFieldValue(mgr, "_isAddZoneFlowActive"),
                "Successful confirm must end the flow even via the fallback path.");
        }

        // ── ShowAddZoneDialog: template toggle state ─────────────────────────────

        [Test]
        public void MapEditorUI_ShowAddZoneDialog_NoSource_TemplateToggleOffAndDisabled()
        {
            var ui = CreateInitializedUI();
            ui.ShowAddZoneDialog("zone_150_150", sourceZoneName: "", sourceEditable: false);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assume.That(refs.AddUseTemplateToggle != null,
                "AddUseTemplateToggle must be wired by BuildAll.");

            Assert.IsFalse(refs.AddUseTemplateToggle.isOn,
                "Template toggle must default OFF when there is no source zone.");
            Assert.IsFalse(refs.AddUseTemplateToggle.interactable,
                "Template toggle must be non-interactable when there is no source — it has nothing to template from.");
        }

        [Test]
        public void MapEditorUI_ShowAddZoneDialog_WithSource_TemplateToggleOnAndInteractable()
        {
            var ui = CreateInitializedUI();
            ui.ShowAddZoneDialog("zone_150_150", sourceZoneName: "Alpha", sourceEditable: true);

            var refs = (MapEditorUIBuilder.UIRefs) GetFieldValue(ui, "_refs");
            Assume.That(refs.AddUseTemplateToggle != null,
                "AddUseTemplateToggle must be wired by BuildAll.");

            Assert.IsTrue(refs.AddUseTemplateToggle.isOn,
                "Template toggle must default ON when a source zone is provided.");
            Assert.IsTrue(refs.AddUseTemplateToggle.interactable,
                "Template toggle must be interactable when a source is available.");
        }

        // ── GenerateOffsetZoneName ────────────────────────────────────────────────

        [Test]
        public void GenerateOffsetZoneName_ReturnsOffsetBasedName()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));

            string name = (string) typeof(MapEditorManager)
                .GetMethod("GenerateOffsetZoneName",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mgr, new object[] { new Vector2Int(150, 150) });

            Assert.AreEqual("zone_150_150", name,
                "Offset (150,150) must produce the canonical name 'zone_150_150'.");
        }

        [Test]
        public void GenerateOffsetZoneName_HandlesNegativeOffset()
        {
            var mgr = CreateManagerWithZones(("Alpha", Vector2Int.zero, true));

            string name = (string) typeof(MapEditorManager)
                .GetMethod("GenerateOffsetZoneName",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mgr, new object[] { new Vector2Int(-50, 100) });

            Assert.AreEqual("zone_-50_100", name,
                "Negative offsets must be preserved in the generated name (no abs / no underscore-trick).");
        }

        [Test]
        public void GenerateOffsetZoneName_AppendsSuffixWhenBaseNameCollides()
        {
            var mgr = CreateManagerWithZones(
                ("Alpha",         Vector2Int.zero,           true),
                ("zone_150_150",  new Vector2Int(150, 150),  true));

            string name = (string) typeof(MapEditorManager)
                .GetMethod("GenerateOffsetZoneName",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mgr, new object[] { new Vector2Int(150, 150) });

            Assert.AreNotEqual("zone_150_150", name,
                "Generator must not return a name that already exists.");
            Assert.IsTrue(name.StartsWith("zone_150_150"),
                $"Suffixed name must keep the offset prefix. Got '{name}'.");
            Assert.IsFalse(GetZM(mgr).TryGetZone(name, out _),
                "Suffixed name must not already exist in ZoneManager.");
        }

        // ── External overlay sharing (Tile Editor → Map Editor zone borders) ────
        //
        // The Tile Editor (F8) requests that the Map Editor's zone-border
        // overlay be shown so the user can see zone delimitations while
        // painting. The Map Editor must honour the request without activating
        // its own UI, and must hide the overlay only once both signals
        // (its own _state.Active AND the external request) are false.

        [Test]
        public void MapEditorManager_SetExternalOverlayRequest_True_ShowsOverlayWhileEditorInactive()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("ExtOverlayMgr");
            InvokeMethod(mgr, "CreateOverlayRoot");

            mgr.SetExternalOverlayRequest(true);

            var overlayRoot = (GameObject) GetFieldValue(mgr, "_overlayRoot");
            Assert.IsNotNull(overlayRoot, "_overlayRoot must be created by CreateOverlayRoot.");
            Assert.IsTrue(overlayRoot.activeSelf,
                "Overlay root must become active when an external editor requests it, " +
                "even when the Map Editor itself is not active.");
        }

        [Test]
        public void MapEditorManager_SetExternalOverlayRequest_False_HidesOverlayWhenEditorAlsoInactive()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("ExtOverlayMgr");
            InvokeMethod(mgr, "CreateOverlayRoot");

            mgr.SetExternalOverlayRequest(true);
            mgr.SetExternalOverlayRequest(false);

            var overlayRoot = (GameObject) GetFieldValue(mgr, "_overlayRoot");
            Assert.IsFalse(overlayRoot.activeSelf,
                "Overlay must hide once the external request is released and the Map Editor itself is inactive.");
        }

        [Test]
        public void MapEditorManager_SetExternalOverlayRequest_False_KeepsOverlayWhileMapEditorActive()
        {
            LogAssert.ignoreFailingMessages = true;
            var mgr = CreateSingleton<MapEditorManager>("ExtOverlayMgr");
            InvokeMethod(mgr, "CreateOverlayRoot");

            // Activate the Map Editor's own state (skip ToggleActive — it
            // touches UI and zoneManager which aren't wired in EditMode).
            var state = (Valkur.Gameplay.MapEditor.MapEditorState) GetFieldValue(mgr, "_state");
            state.Active = true;
            InvokeMethod(mgr, "UpdateOverlayVisibility");

            mgr.SetExternalOverlayRequest(true);
            mgr.SetExternalOverlayRequest(false);

            var overlayRoot = (GameObject) GetFieldValue(mgr, "_overlayRoot");
            Assert.IsTrue(overlayRoot.activeSelf,
                "Overlay must stay visible while the Map Editor itself is active, " +
                "even after the external request is released.");
        }

        // ── MapEditorInputHandler centralization regression ─────────────────────
        //
        // The handler was refactored to route mouse/keyboard polling through
        // MouseInputManager / KeyboardInputManager (the centralized facades
        // that OR new+legacy backends to survive Unity 2022.3 InputSystem
        // event drops). The old ad-hoc InputAction fields must NOT come back
        // — they would silently die under the bug. The toggle action stays
        // because EditorHotkeyBindings already routes through the canonical
        // InputService asset.

        [Test]
        public void MapEditorInputHandler_DoesNotOwnAdHocSelectAction()
        {
            var handler = new MapEditorInputHandler();
            Assert.IsNull(handler.GetType().GetField("_selectAction",
                BindingFlags.NonPublic | BindingFlags.Instance),
                "_selectAction must NOT exist after the centralized-facade refactor — " +
                "click polling routes through MouseInputManager.WasLeftMouseButtonPressedThisFrame().");
        }

        [Test]
        public void MapEditorInputHandler_DoesNotOwnAdHocKeyboardActions()
        {
            var handler = new MapEditorInputHandler();
            string[] obsolete = {
                "_createAction", "_duplicateAction", "_deleteAction",
                "_renameAction", "_toggleEditableAction"
            };
            foreach (var fieldName in obsolete)
            {
                Assert.IsNull(handler.GetType().GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance),
                    $"{fieldName} must NOT exist after the centralized-facade refactor — " +
                    "keyboard polling routes through KeyboardInputManager.WasKeyPressedThisFrame().");
            }
        }

        [Test]
        public void MapEditorInputHandler_PollMethodsDoNotThrow_BeforeCreateActions()
        {
            // Even if CreateActions() hasn't been called (or the facades have
            // no live input device, which is the EditMode reality), every
            // Was*Pressed query must return a value safely — the underlying
            // facades guard against null Mouse/Keyboard.current themselves.
            var handler = new MapEditorInputHandler();
            Assert.DoesNotThrow(() => handler.WasSelectPressed(),
                "WasSelectPressed must be safe to call before any input device exists.");
            Assert.DoesNotThrow(() => handler.WasCreatePressed());
            Assert.DoesNotThrow(() => handler.WasDuplicatePressed());
            Assert.DoesNotThrow(() => handler.WasDeletePressed());
            Assert.DoesNotThrow(() => handler.WasRenamePressed());
            Assert.DoesNotThrow(() => handler.WasToggleEditablePressed());
        }
    }
}
