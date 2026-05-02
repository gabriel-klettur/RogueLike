using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;
using Valkur.Gameplay.TileEditor;
using Valkur.Gameplay.World;

namespace Valkur.Gameplay.Buildings
{
    public partial class BuildingsRuntimeEditor : SingletonMonoBehaviour<BuildingsRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void SetMode(EditorMode mode)
        {
            _mode = mode;
            if (_mode != EditorMode.Resize) _resizing = false;
            // Leaving Fill mode via SetMode (e.g. switching to Select externally)
            // must clean up Fill state without re-entering SetMode recursively.
            if (_mode != EditorMode.Fill && _fillStep != FillStep.Idle)
                ExitFillMode(setSelectMode: false);
            // Same guard for Erase: any external mode switch must tear down Erase state.
            if (_mode != EditorMode.Erase && _eraseStep != EraseStep.Idle)
                ExitEraseMode(setSelectMode: false);
            RefreshModeButtons();
            if (_statusTmp == null) return;
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select: click building on map. Wheel to cycle stack. Drag thumbnails from the Buildings panel to place new ones.",
                EditorMode.Place  => "Placement is drag-only: drag a thumbnail from the Buildings panel onto the map.",
                EditorMode.Delete => "Click building to delete (with confirm).",
                EditorMode.Resize => "LMB-drag the R handle (top-right) to resize proportionally.",
                EditorMode.Fill   => "Fill: enter spacing, pick a template, then hover and click to flood-fill.",
                EditorMode.Erase  => "Erase: pick scope (Tiles Area / Zone), then click a building to delete all of its type in that scope.",
                _ => ""
            };
        }

        private void RefreshModeButtons()
        {
            if (_selectBtnImg) _selectBtnImg.color = _mode == EditorMode.Select ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_placeBtnImg)  _placeBtnImg.color  = _mode == EditorMode.Place  ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_resizeBtnImg) _resizeBtnImg.color = _mode == EditorMode.Resize ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_deleteBtnImg) _deleteBtnImg.color = _mode == EditorMode.Delete ? EditorUIHelpers.DANGER     : new Color(0.55f, 0.15f, 0.15f, 1f);
            if (_addBtnImg)    _addBtnImg.color    = _mode == EditorMode.Place  ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_removeBtnImg) _removeBtnImg.color = _removeMode                ? EditorUIHelpers.DANGER     : new Color(0.55f, 0.15f, 0.15f, 1f);
            if (_fillBtnImg)   _fillBtnImg.color   = _mode == EditorMode.Fill   ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
            if (_eraseBtnImg)  _eraseBtnImg.color  = _mode == EditorMode.Erase  ? EditorUIHelpers.BTN_ACTIVE : EditorUIHelpers.BTN_NORMAL;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  ADD / REMOVE PANEL CALLBACKS
        // ──────────────────────────────────────────────────────────────────────────

        private void OnAddBuildingClicked()
        {
            // Placement is drag-only. The Add (+) button no longer enters a
            // "click-to-place" mode — it just reminds the user how to place.
            Toast(_selectedTemplateId >= 0
                ? $"Drag template #{_selectedTemplateId} from the Buildings panel onto the map to place it."
                : "Pick a template from the Buildings panel and DRAG it onto the map to place.");
        }

        private void ToggleRemoveMode()
        {
            _removeMode = !_removeMode;
            if (_removeMode) SetMode(EditorMode.Delete);
            RefreshModeButtons();
            Toast(_removeMode ? "Remove mode ON. Click building to delete." : "Remove mode OFF.");
        }

        private void OnAddOnSystemClicked()
        {
            // Python's add_building_on_system tool: opens a system-level placer (e.g.
            // file system browser to drop an external image as a new template).
            // Phase 2 — surface a status message for now so users know it's wired.
            Toast("Add-on-system: import external sprite as template (TODO Phase 2).");
        }

        private void ToggleCollidersMode()
        {
            // Python toggles colliders_mode which hides handles and exposes paint UI.
            // We surface this through the inspector (always visible) and just switch
            // mode label; deeper paint logic is Phase 2.
            Toast("Colliders mode toggled (paint UI in inspector — Phase 2).");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  INTERACTION (mouse + keyboard)
        // ──────────────────────────────────────────────────────────────────────────

        // Middle-mouse camera pan is handled by the shared EditorCameraPanController
        // (Scripts/Gameplay/Editors/EditorCameraPanController.cs). The previous
        // ~35-line implementation lived here and was duplicated in TileEditorManager
        // and MapEditorManager.
        private void HandleCameraPan() => _cameraPan.Tick();

        private void HandleKeyboardShortcuts()
        {
            // Defense in depth: Update() already gates this with `if (!_active) return;`,
            // but a second guard here makes Ctrl+Z / Ctrl+Y a strict no-op if the editor
            // is closed (matches the user-facing rule "no editor open → Ctrl+Z does nothing").
            if (!_active) return;

            // Routed through KeyboardInputManager so the legacy backend supplies
            // these reads when the new InputSystem package drops OS events
            // (recurring Unity 2022.3 Editor bug).
            bool ctrl = Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld();
            if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z)) _undo.Undo();
            if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y)) _undo.Redo();
            if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.S, KeyCode.S)) SaveInstancesToJson();
            if (Valkur.Core.Input.KeyboardInputManager.WasDeletePressedThisFrame() && _activeBuilding != null) RequestDeleteActiveWithConfirm();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.D, KeyCode.D) && _activeBuilding != null && !ctrl) ResetActiveBuilding();
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.R, KeyCode.R) && _activeBuilding != null) SetMode(EditorMode.Resize);
            if (Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
            {
                if (_confirmModal != null && _confirmModal.activeSelf) HideConfirm();
                else if (_fillSpacingModal != null && _fillSpacingModal.activeSelf) ExitFillMode();
                else if (_mode == EditorMode.Fill) ExitFillMode();
                else if (_eraseConfirmModal != null && _eraseConfirmModal.activeSelf) ExitEraseMode();
                else if (_mode == EditorMode.Erase) ExitEraseMode();
                else if (_tutorialRoot != null && _tutorialRoot.activeSelf) _tutorialRoot.SetActive(false);
                else { SaveInstancesToJson(); Deactivate(); }
            }

            // Colliders panel shortcuts — only active when the panel is open so we
            // never steal keys (especially '.') from other systems while not editing
            // colliders. All keys are explicitly read; pressing them while the panel
            // is open consumes the action regardless of any other listeners.
            if (_openDropdowns.Contains("colliders"))
                HandleColliderEditorShortcuts();
        }

        private void HandleColliderEditorShortcuts()
        {
            // All shortcut reads route through KeyboardInputManager so the
            // legacy backend keeps them working under the InputSystem-drops-events
            // bug. Same OR-fallback pattern used everywhere else in Valkur.
            bool ctrl = Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld();

            // B → toggle brush ON/OFF
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.B, KeyCode.B) && !ctrl)
                SetBrushOn(!BrushOn);

            // # (Shift+3) or numpad-3 → action = Paint (writes "#")
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Digit3, KeyCode.Alpha3)
                && Valkur.Core.Input.KeyboardInputManager.IsShiftHeld())
                SetBrushAction(CollBrushMode.Solid);
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Numpad3, KeyCode.Keypad3))
                SetBrushAction(CollBrushMode.Solid);

            // . (period) or numpad-. → action = Erase (writes ".")
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Period, KeyCode.Period)
                || Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.NumpadPeriod, KeyCode.KeypadPeriod))
                SetBrushAction(CollBrushMode.Walk);

            // [ / ] → brush size −/+
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.LeftBracket, KeyCode.LeftBracket))
                OnCollBrushSizeChanged(_collBrushSize - 1);
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.RightBracket, KeyCode.RightBracket))
                OnCollBrushSizeChanged(_collBrushSize + 1);

            // Tab → toggle scope CG ↔ CU on the active building
            if (Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Tab, KeyCode.Tab) && _activeBuilding != null)
                ToggleColliderScope();
        }

    }
}