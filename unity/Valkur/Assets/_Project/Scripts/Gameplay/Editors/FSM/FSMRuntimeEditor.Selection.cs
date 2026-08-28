using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Gameplay.Editors;
using Valkur.UIKit;

namespace Valkur.Gameplay.Enemies.FSM
{
    public partial class FSMRuntimeEditor : SingletonMonoBehaviour<FSMRuntimeEditor>, GameEditorManager.IGameEditor
    {

        private void ApplyZoomPan()
        {
            if (_graphContent == null) return;
            _graphContent.localScale = Vector3.one * _zoom;
            _graphContent.anchoredPosition = _pan;
        }

        // â”€â”€ Graph Input (Pan/Zoom) â”€â”€

        // Zoom clamp shared by the wheel and the toolbar ± buttons — previously the
        // wheel clamped to [0.3, 2.0] while AdjustZoom clamped to [0.25, 3.0], so
        // alternating inputs made the zoom "jump" at the boundaries.
        private const float ZOOM_MIN = 0.25f;
        private const float ZOOM_MAX = 3f;

        /// <summary>
        /// True when the screen-space pointer is over the graph canvas area. Guards
        /// both wheel-zoom and MMB-pan: without it, scrolling the Sets/Properties
        /// panels ALSO zoomed the graph (the ScrollRect scrolled the list while this
        /// method zoomed the canvas), and middle-clicking a panel header started a
        /// graph pan behind the drag. Null camera matches the overlay canvas —
        /// RectangleContainsScreenPoint handles that like the rest of this file.
        /// </summary>
        private bool IsPointerOverGraphArea()
        {
            if (_graphContent == null) return false;
            var canvasArea = _graphContent.parent as RectTransform;
            if (canvasArea == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                canvasArea,
                Valkur.Core.Input.MouseInputManager.GetScreenMousePosition(),
                _canvas != null ? _canvas.worldCamera : null);
        }

        /// <summary>
        /// Single zoom entry point: clamps, syncs the toolbar % label (the wheel path
        /// used to skip it, desyncing the label from the real zoom) and repaints.
        /// Both the wheel below and <c>AdjustZoom</c> route through here.
        /// </summary>
        private void SetZoom(float value)
        {
            _zoom = Mathf.Clamp(value, ZOOM_MIN, ZOOM_MAX);
            if (_uiRefs.GraphZoomLabel != null)
                _uiRefs.GraphZoomLabel.text = $"{Mathf.RoundToInt(_zoom * 100f)}%";
            ApplyZoomPan();
        }

        private void HandleGraphInput()
        {
            var mouse = Mouse.current;

            // Zoom with scroll wheel — ONLY when the pointer is over the graph canvas.
            // The old comment claimed that guard existed; it did not. (The test env:
            // MouseInputManager.GetMouseWheelDelta() ORs new + legacy backends so the
            // graph keeps zooming when the new InputSystem package drops events.)
            float scrollDelta = Valkur.Core.Input.MouseInputManager.GetMouseWheelDelta();
            if (Mathf.Abs(scrollDelta) > 0.01f && IsPointerOverGraphArea())
            {
                SetZoom(_zoom + scrollDelta * 0.001f);
            }

            // Pan with MMB — same containment rule as zoom.
            if (Valkur.Core.Input.MouseInputManager.WasMiddleMouseButtonPressedThisFrame()
                && IsPointerOverGraphArea())
            {
                _panning = true;
                _panStart = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition() - _pan;
            }
            if (Valkur.Core.Input.MouseInputManager.WasMiddleMouseButtonReleasedThisFrame())
                _panning = false;

            if (_panning)
            {
                _pan = (Vector2)Valkur.Core.Input.MouseInputManager.GetScreenMousePosition() - _panStart;
                ApplyZoomPan();
            }

            // Drag selected node with LMB
            if (_selectedState != null && _draggingNode)
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() && _nodeRects.TryGetValue(_selectedState.id, out var nrt))
                {
                    var delta = (Vector2)mouse.delta.ReadValue() / _zoom;
                    _selectedState.x += delta.x;
                    _selectedState.y -= delta.y;
                    nrt.anchoredPosition = new Vector2(_selectedState.x, -_selectedState.y);
                }
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
                {
                    _draggingNode = false;
                    PersistSets();                  // persist new x/y inside set raw
                    PersistLayoutForSelectedSet();  // mirror into layouts.json
                    RefreshGraph();                 // Redraw edges
                }
            }

            // Empty-canvas click â†’ tool-aware (Add/cancel-pending)
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame() && !_draggingNode &&
                TryGetEmptyCanvasContentPos(out var localPos))
            {
                OnEmptyCanvasClicked(localPos);
            }
        }

        // â”€â”€ Selection â”€â”€

        private void SelectState(FSMStateNode state)
        {
            _selectedState = state;
            _selectedTransition = null;
            _propsTab = PropsTab.State;
            _draggingNode = true;
            RefreshTabs();
            RefreshProperties();
            RefreshGraph();
        }

        private void SelectTransition(FSMTransitionData trans)
        {
            _selectedTransition = trans;
            _selectedState = null;
            _propsTab = PropsTab.Transition;
            RefreshTabs();
            RefreshProperties();
            RefreshGraph();
        }

        // â”€â”€ Properties â”€â”€

        private void RefreshProperties()
        {
            RebuildPropertiesContent();
        }

        // â”€â”€ Live Entity Inspection â”€â”€

        /// <summary>
        /// Called by external systems to inspect the FSM of a specific entity at runtime.
        /// </summary>
        public void InspectEntity(GameObject entity)
        {
            var brain = entity.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
            if (brain == null) return;

            SetStatus($"Inspecting: {entity.name} â€” State: {brain.CurrentStateName}");
        }
    }
}