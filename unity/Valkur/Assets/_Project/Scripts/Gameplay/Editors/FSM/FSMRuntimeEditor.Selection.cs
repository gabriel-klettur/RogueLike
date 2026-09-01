using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        /// Screen pixels the pointer must travel FROM THE PRESS POSITION before a node
        /// drag starts. Left-click means both "select" and "drag" here, so without a
        /// dead zone every selection click nudged the node by whatever the mouse moved
        /// while the button was down.
        /// </summary>
        private const float DRAG_THRESHOLD = 6f;

        /// <summary>Set on a left-press that lands on the selected node — the only press
        /// that may become a drag. A press anywhere else leaves it false, so panning past
        /// a selected node or clicking empty canvas can never move it.</summary>
        private bool _nodeDragArmed;

        /// <summary>Screen position of the press that armed the drag.</summary>
        private Vector2 _dragAnchorMouse;

        /// <summary>Node position (graph space) at that same press. The drag is applied as
        /// anchor + travel rather than accumulated per-frame deltas: accumulation drifts
        /// away from the pointer across dropped frames and cannot be undone by moving back.</summary>
        private Vector2 _dragAnchorNode;

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

        /// <summary>
        /// True when the screen pointer is inside the drawn rect of the node with this id.
        /// The rects live under the zoomed and panned content;
        /// <c>RectangleContainsScreenPoint</c> works from the rendered corners, so no zoom
        /// compensation belongs here. Null camera matches the overlay canvas, as everywhere
        /// else in this file.
        /// </summary>
        private bool IsPointerOverNode(string stateId)
        {
            if (string.IsNullOrEmpty(stateId)) return false;
            if (!_nodeRects.TryGetValue(stateId, out var nrt) || nrt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                nrt,
                Valkur.Core.Input.MouseInputManager.GetScreenMousePosition(),
                _canvas != null ? _canvas.worldCamera : null);
        }

        private void HandleGraphInput()
        {
            // Every read below goes through MouseInputManager — the drag used to reach for
            // Mouse.current.delta directly, which is one of the few direct reads CLAUDE.md
            // still tolerates and the one backend that drops events in the 2022.3 Editor.
            // Screen-position travel needs neither the exception nor the null check.

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

            // Drag the selected node with LMB. Three steps, one per button phase: arm on a
            // press that lands on the node, promote to a drag once the pointer has left the
            // DRAG_THRESHOLD dead zone, persist on release. The dead zone is measured from
            // the press position — a per-FRAME delta test never fires on a slow drag, since
            // a hand moving 60 px/s covers one pixel per frame.
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                _nodeDragArmed = _selectedState != null
                              && IsPointerOverGraphArea()
                              && IsPointerOverNode(_selectedState.id);
                _dragAnchorMouse = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
                _dragAnchorNode = _selectedState != null
                    ? new Vector2(_selectedState.x, _selectedState.y)
                    : Vector2.zero;
            }

            if (_nodeDragArmed && _selectedState != null
                && Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
            {
                Vector2 travel = (Vector2)Valkur.Core.Input.MouseInputManager.GetScreenMousePosition()
                                 - _dragAnchorMouse;

                if (!_draggingNode && travel.magnitude >= DRAG_THRESHOLD)
                    _draggingNode = true;

                if (_draggingNode && _nodeRects.TryGetValue(_selectedState.id, out var nrt))
                {
                    // Graph content is scaled by _zoom, so screen travel is divided by it —
                    // the node then tracks the cursor 1:1 at every zoom level. y is negated
                    // because the layout stores it top-down while the rect grows upward.
                    _selectedState.x = _dragAnchorNode.x + travel.x / _zoom;
                    _selectedState.y = _dragAnchorNode.y - travel.y / _zoom;
                    nrt.anchoredPosition = new Vector2(_selectedState.x, -_selectedState.y);
                }
            }

            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                _nodeDragArmed = false;
                if (_draggingNode)
                {
                    _draggingNode = false;
                    PersistSets();                  // persist new x/y inside set raw
                    PersistLayoutForSelectedSet();  // mirror into layouts.json
                    RefreshGraph();                 // redraw edges at the new position
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
            // Selection does NOT start a drag. This runs from the node Button's onClick,
            // which uGUI raises on pointer UP — so the old `_draggingNode = true` here armed
            // a drag with the button already released, and the node then followed the next
            // press anywhere on the canvas. HandleGraphInput owns the drag now: it arms on a
            // press over this node and promotes to a real drag past DRAG_THRESHOLD.
            _draggingNode = false;
            _nodeDragArmed = false;
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