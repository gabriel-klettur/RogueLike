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
using Valkur.Gameplay.Editors.EditorKit;
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
            RefreshModeButtons();
            if (_statusTmp == null) return;
            _statusTmp.text = _mode switch
            {
                EditorMode.Select => "Select: click building on map. Wheel to cycle stack. Drag thumbnails from the Buildings panel to place new ones.",
                EditorMode.Place  => "Placement is drag-only: drag a thumbnail from the Buildings panel onto the map.",
                EditorMode.Delete => "Click building to delete (with confirm).",
                EditorMode.Resize => "LMB-drag the R handle (top-right) to resize proportionally.",
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

        // ── Middle-mouse camera pan ──────────────────────────────────────────────────
        // Mirrors TileEditorManager.HandleCameraPan() and Python camera_pan.py.
        //   MMB press   → save vcam anchor
        //   MMB held    → offset vcam from anchor by screen-space delta
        //   MMB release → stop panning
        private void HandleCameraPan()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            var camSetup = Valkur.Gameplay.CameraSetup.Instance;
            Transform vcamT = camSetup != null ? camSetup.GetDetachedTransform() : null;
            if (vcamT == null) return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                _isPanning = true;
                _panAnchorScreenPos = mouse.position.ReadValue();
                _panAnchorCamPos = vcamT.position;
            }
            else if (mouse.middleButton.wasReleasedThisFrame)
            {
                _isPanning = false;
            }

            if (_isPanning && mouse.middleButton.isPressed)
            {
                Vector2 currentScreenPos = mouse.position.ReadValue();
                Vector2 screenDelta = currentScreenPos - _panAnchorScreenPos;

                float unitsPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
                Vector3 worldDelta = new Vector3(screenDelta.x, screenDelta.y, 0f) * unitsPerPixel;
                Vector3 newPos = _panAnchorCamPos - worldDelta;
                newPos.z = vcamT.position.z;
                vcamT.position = newPos;
            }
        }

        private void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool ctrl = kb.ctrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) _undo.Undo();
            if (ctrl && kb.yKey.wasPressedThisFrame) _undo.Redo();
            if (ctrl && kb.sKey.wasPressedThisFrame) SaveInstancesToJson();
            if (kb.deleteKey.wasPressedThisFrame && _activeBuilding != null) RequestDeleteActiveWithConfirm();
            if (kb.dKey.wasPressedThisFrame && _activeBuilding != null && !ctrl) ResetActiveBuilding();
            if (kb.rKey.wasPressedThisFrame && _activeBuilding != null) SetMode(EditorMode.Resize);
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_confirmModal != null && _confirmModal.activeSelf) HideConfirm();
                else if (_tutorialRoot != null && _tutorialRoot.activeSelf) _tutorialRoot.SetActive(false);
                else { SaveInstancesToJson(); Deactivate(); }
            }

            // Colliders panel shortcuts — only active when the panel is open so we
            // never steal keys (especially '.') from other systems while not editing
            // colliders. All keys are explicitly read; pressing them while the panel
            // is open consumes the action regardless of any other listeners.
            if (_openDropdowns.Contains("colliders"))
                HandleColliderEditorShortcuts(kb);
        }

        private void HandleColliderEditorShortcuts(Keyboard kb)
        {
            // B → toggle brush ON/OFF
            if (kb.bKey.wasPressedThisFrame && !kb.ctrlKey.isPressed)
                SetBrushOn(!BrushOn);

            // # (Shift+3) or numpad-3 → action = Paint (writes "#")
            if (kb.digit3Key.wasPressedThisFrame && kb.shiftKey.isPressed)
                SetBrushAction(CollBrushMode.Solid);
            if (kb.numpad3Key.wasPressedThisFrame)
                SetBrushAction(CollBrushMode.Solid);

            // . (period) or numpad-. → action = Erase (writes ".")
            if (kb.periodKey.wasPressedThisFrame || kb.numpadPeriodKey.wasPressedThisFrame)
                SetBrushAction(CollBrushMode.Walk);

            // [ / ] → brush size −/+
            if (kb.leftBracketKey.wasPressedThisFrame)
                OnCollBrushSizeChanged(_collBrushSize - 1);
            if (kb.rightBracketKey.wasPressedThisFrame)
                OnCollBrushSizeChanged(_collBrushSize + 1);

            // Tab → toggle scope CG ↔ CU on the active building
            if (kb.tabKey.wasPressedThisFrame && _activeBuilding != null)
                ToggleColliderScope();
        }

    }
}