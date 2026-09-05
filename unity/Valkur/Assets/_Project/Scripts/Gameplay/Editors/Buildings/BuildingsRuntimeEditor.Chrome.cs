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

        // ──────────────────────────────────────────────────────────────────────────
        //  Chrome that used to live in Persistence.cs: confirm modal, tutorial,
        //  buildings visibility, and the per-frame outline / handle updates. Moved
        //  in the 2026-09-05 audit so the persistence file holds persistence.
        // ──────────────────────────────────────────────────────────────────────────

        // ──────────────────────────────────────────────────────────────────────────
        //  CONFIRM MODAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ShowConfirm(string text, System.Action onYes)
        {
            if (_confirmModal == null) { onYes?.Invoke(); return; }
            _confirmText.text = text;
            _pendingConfirmYes = onYes;
            _confirmModal.SetActive(true);
            _confirmModal.transform.SetAsLastSibling();
        }

        private void HideConfirm()
        {
            _pendingConfirmYes = null;
            if (_confirmModal != null) _confirmModal.SetActive(false);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  TUTORIAL
        // ──────────────────────────────────────────────────────────────────────────

        private void ToggleTutorial()
        {
            if (_tutorialRoot == null) return;
            bool show = !_tutorialRoot.activeSelf;
            _tutorialRoot.SetActive(show);
            if (show) { _tutorialRoot.transform.SetAsLastSibling(); RefreshTutorial(); }
        }

        private void StepTutorial(int delta)
        {
            _tutorialStep = (_tutorialStep + delta + TUTORIAL_STEPS.Length) % TUTORIAL_STEPS.Length;
            RefreshTutorial();
        }

        private void RefreshTutorial()
        {
            if (_tutorialStepLabel == null) return;
            var (title, body) = TUTORIAL_STEPS[_tutorialStep];
            _tutorialStepLabel.text = $"{title}   ({_tutorialStep + 1}/{TUTORIAL_STEPS.Length})";
            _tutorialBodyTmp.text = body;
        }

        private void ToggleBuildingsVisible()
        {
            _buildingsVisible = !_buildingsVisible;
            ApplyBuildingsVisibility();
            RefreshBuildingsVisibilityButton();

            if (!_buildingsVisible)
            {
                _hoveredBuilding = null;
                _hoverStack.Clear();
            }

            if (_statusTmp != null)
                _statusTmp.text = _buildingsVisible ? "Buildings visible." : "Buildings hidden.";
        }

        private void ApplyBuildingsVisibility()
        {
            var all = GetCachedBuildings();
            for (int i = 0; i < all.Length; i++)
            {
                var building = all[i];
                if (building == null) continue;

                var renderers = building.GetComponentsInChildren<SpriteRenderer>(true);
                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        renderers[j].enabled = _buildingsVisible;
                }
            }

            if (!_buildingsVisible)
                HideOutlines();
        }

        private void RefreshBuildingsVisibilityButton()
        {
            BuildingsEditorUIBuilder.ApplyMenuBtnStyle(
                _uiRefs.BuildingVisibilityMenuBtnImg,
                _uiRefs.BuildingVisibilityMenuBtnTmp,
                _buildingsVisible);
        }

        // ──────────────────────────────────────────────────────────────────────────
        //  PER-FRAME OVERLAY UPDATES (outlines + handles + ID label)
        // ──────────────────────────────────────────────────────────────────────────

        private void UpdateOutlineState()
        {
            if (_hoverFx == null || _activeFx == null) return;
            if (!_buildingsVisible)
            {
                HideOutlines();
                return;
            }

            // Hover (skip if same as active to avoid double-drawing)
            if (_hoveredBuilding != null && _hoveredBuilding != _activeBuilding)
            {
                bool red = _removeMode || _mode == EditorMode.Delete;
                _hoverFx.Configure(
                    color:        red ? HOVER_REMOVE_RED : HOVER_CYAN,
                    thicknessWorld: red ? HOVER_THICKNESS_WORLD * 1.5f : HOVER_THICKNESS_WORLD,
                    drawFill:     red,
                    fillColor:    HOVER_REMOVE_FILL);
                _hoverFx.Follow(_hoveredBuilding);
            }
            else
            {
                _hoverFx.Follow(null); _hoverFx.SetVisible(false);
            }

            // Active
            if (_activeBuilding != null) _activeFx.Follow(_activeBuilding);
            else { _activeFx.Follow(null); _activeFx.SetVisible(false); }
        }

        private void UpdateFloatingHandles()
        {
            if (_handlesRoot == null) return;
            if (!_buildingsVisible)
            {
                _handlesRoot.SetActive(false);
                return;
            }
            bool show = _activeBuilding != null && !_removeMode;
            _handlesRoot.SetActive(show);
            if (!show) return;

            if (!_activeBuilding.TryGetWorldRect(out var rect)) { _handlesRoot.SetActive(false); return; }
            var cam = Camera.main;
            if (cam == null) return;

            // Project building top-right corner to canvas (pivot=top-right → badge sits inside frame)
            Vector3 worldTopRight = new Vector3(rect.xMax, rect.yMax, 0f);
            Vector3 screenTR      = cam.WorldToScreenPoint(worldTopRight);
            Vector2 canvasTR      = ScreenToCanvasPos(screenTR);

            // Compute proportional badge size from the building's canvas-space width
            Vector3 worldTopLeft = new Vector3(rect.xMin, rect.yMax, 0f);
            Vector3 screenTL     = cam.WorldToScreenPoint(worldTopLeft);
            Vector2 canvasTL     = ScreenToCanvasPos(screenTL);
            float canvasW        = Mathf.Abs(canvasTR.x - canvasTL.x);
            float handleSize     = Mathf.Clamp(canvasW * 0.20f, 20f, 52f);

            var rt = _handlesRoot.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(handleSize, handleSize);
            rt.anchoredPosition = canvasTR;
        }
    }
}
