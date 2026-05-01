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

        // ── Graph Input (Pan/Zoom) ──

        private void HandleGraphInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Zoom with scroll wheel (when pointer is over graph area)
            var scrollDelta = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollDelta) > 0.01f)
            {
                _zoom = Mathf.Clamp(_zoom + scrollDelta * 0.001f, 0.3f, 2f);
                ApplyZoomPan();
            }

            // Pan with MMB
            if (mouse.middleButton.wasPressedThisFrame)
            {
                _panning = true;
                _panStart = mouse.position.ReadValue() - _pan;
            }
            if (mouse.middleButton.wasReleasedThisFrame)
                _panning = false;

            if (_panning)
            {
                _pan = (Vector2)mouse.position.ReadValue() - _panStart;
                ApplyZoomPan();
            }

            // Drag selected node with LMB
            if (_selectedState != null && _draggingNode)
            {
                if (mouse.leftButton.isPressed && _nodeRects.TryGetValue(_selectedState.id, out var nrt))
                {
                    var delta = (Vector2)mouse.delta.ReadValue() / _zoom;
                    _selectedState.x += delta.x;
                    _selectedState.y -= delta.y;
                    nrt.anchoredPosition = new Vector2(_selectedState.x, -_selectedState.y);
                }
                if (mouse.leftButton.wasReleasedThisFrame)
                {
                    _draggingNode = false;
                    PersistSets();                  // persist new x/y inside set raw
                    PersistLayoutForSelectedSet();  // mirror into layouts.json
                    RefreshGraph();                 // Redraw edges
                }
            }

            // Empty-canvas click → tool-aware (Add/cancel-pending)
            if (mouse.leftButton.wasPressedThisFrame && !_draggingNode &&
                TryGetEmptyCanvasContentPos(out var localPos))
            {
                OnEmptyCanvasClicked(localPos);
            }
        }

        // ── Selection ──

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

        // ── Properties ──

        private void RefreshProperties()
        {
            RebuildPropertiesContent();
        }

        private void ShowStateProperties()
        {
            if (_selectedState == null)
            {
                _propsTmp.text = "Click a state node to view properties.";
                return;
            }
            var s = _selectedState;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>STATE PROPERTIES</b>");
            sb.AppendLine();
            sb.AppendLine($"<b>ID:</b> {s.id}");
            sb.AppendLine($"<b>Label:</b> {s.label}");
            sb.AppendLine($"<b>Class:</b> {s.stateClass}");
            sb.AppendLine($"<b>Initial:</b> {s.isInitial || (_selectedSet?.initial == s.id)}");
            sb.AppendLine($"<b>Terminal:</b> {s.isTerminal}");
            sb.AppendLine($"<b>Position:</b> ({s.x:F0}, {s.y:F0})");
            sb.AppendLine($"<b>Size:</b> {s.w:F0} × {s.h:F0}");

            // Show outgoing transitions
            if (_selectedSet != null)
            {
                var outgoing = _selectedSet.transitions.Where(t => t.from == s.id).ToList();
                if (outgoing.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine($"<b>Outgoing Transitions ({outgoing.Count}):</b>");
                    foreach (var t in outgoing)
                        sb.AppendLine($"  → {t.to} [{t.label ?? t.whenEvent ?? "?"}]");
                }
                var incoming = _selectedSet.transitions.Where(t => t.to == s.id).ToList();
                if (incoming.Count > 0)
                {
                    sb.AppendLine($"<b>Incoming Transitions ({incoming.Count}):</b>");
                    foreach (var t in incoming)
                        sb.AppendLine($"  ← {t.from} [{t.label ?? t.whenEvent ?? "?"}]");
                }
            }

            _propsTmp.text = sb.ToString();
        }

        private void ShowTransitionProperties()
        {
            if (_selectedTransition == null)
            {
                _propsTmp.text = "Click a transition label to view properties.";
                return;
            }
            var t = _selectedTransition;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<b>TRANSITION PROPERTIES</b>");
            sb.AppendLine();
            sb.AppendLine($"<b>ID:</b> {t.id}");
            sb.AppendLine($"<b>From:</b> {t.from}");
            sb.AppendLine($"<b>To:</b> {t.to}");
            sb.AppendLine($"<b>Label:</b> {t.label}");
            sb.AppendLine($"<b>Event:</b> {t.whenEvent}");
            sb.AppendLine($"<b>Condition:</b> {(string.IsNullOrEmpty(t.condition) ? "–" : t.condition)}");
            sb.AppendLine($"<b>Priority:</b> {t.priority}");
            sb.AppendLine($"<b>Cooldown:</b> {t.cooldownFrames} frames");
            _propsTmp.text = sb.ToString();
        }

        // ── Live Entity Inspection ──

        /// <summary>
        /// Called by external systems to inspect the FSM of a specific entity at runtime.
        /// </summary>
        public void InspectEntity(GameObject entity)
        {
            var brain = entity.GetComponent<Valkur.Gameplay.FSM.FSMMonsterBrain>();
            if (brain == null) return;

            _statusTmp.text = $"Inspecting: {entity.name} — State: {brain.CurrentStateName}";
        }
    }
}