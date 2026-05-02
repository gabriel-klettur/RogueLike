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

        private void HandleMapInteraction()
        {
            // Don't bail when Mouse.current is null — MouseInputManager wraps the
            // legacy backend, which keeps reading even when the new InputSystem
            // package is dropping events. The original `if (mouse == null) return;`
            // suppressed all map interaction under the bug.
            bool overUi = UnityEngine.EventSystems.EventSystem.current != null &&
                          UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

            var cam = Camera.main;
            if (cam == null) return;

            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            Vector3 worldPos  = cam.ScreenToWorldPoint(screenPos);
            worldPos.z = 0f;

            // ── Fill mode: hover computes preview, left-click commits ──────────────
            if (_mode == EditorMode.Fill && _fillStep == FillStep.AwaitingTile)
            {
                if (!overUi) UpdateFillHover(worldPos);
                if (!overUi && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    CommitFill();
                return;
            }

            // ── Erase mode (after scope chosen): left-click picks the target building ─
            if (_mode == EditorMode.Erase && _eraseStep == EraseStep.AwaitingTarget)
            {
                if (!overUi && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    OnEraseTargetClicked(worldPos);
                return;
            }

            if (_colliderStroke.Active && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                EndColliderStroke();
                if (overUi) return;
            }

            // â”€â”€ Hover proximity for split line (always computed, drives highlight colour)
            _splitHovering = false;
            if (!overUi && _activeBuilding != null && _activeBuilding.TryGetWorldRect(out var hoverRect))
            {
                float hsr = _activeBuilding.SplitRatioOverride >= 0f
                    ? _activeBuilding.SplitRatioOverride
                    : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);
                float hSplitY = hoverRect.yMin + hoverRect.height * (1f - hsr);
                _splitHovering = Mathf.Abs(worldPos.y - hSplitY) <= SPLIT_HANDLE_WORLD_RADIUS
                              && worldPos.x >= hoverRect.xMin - SPLIT_HANDLE_WORLD_RADIUS
                              && worldPos.x <= hoverRect.xMax + SPLIT_HANDLE_WORLD_RADIUS;
            }

            // Hover detection (skip when over UI): collect all buildings under cursor.
            if (!_buildingsVisible)
            {
                _hoveredBuilding = null;
                _hoverStack.Clear();
            }
            else if (!overUi) RecomputeHoverStack(worldPos);
            else { _hoveredBuilding = null; _hoverStack.Clear(); }

            // Wheel cycle within hover stack. MouseInputManager.GetMouseWheelDelta()
            // ORs the new + legacy backends so this keeps firing even when the
            // new InputSystem package drops OS events (Unity 2022.3 bug).
            if (!overUi && _hoverStack.Count > 1)
            {
                float scroll = Valkur.Core.Input.MouseInputManager.GetMouseWheelDelta();
                if (scroll >  0.01f) { _hoverIndex = (_hoverIndex - 1 + _hoverStack.Count) % _hoverStack.Count; _hoveredBuilding = _hoverStack[_hoverIndex]; }
                if (scroll < -0.01f) { _hoverIndex = (_hoverIndex + 1) % _hoverStack.Count;                     _hoveredBuilding = _hoverStack[_hoverIndex]; }
            }

            // Split-ratio drag â€” LMB held on the split handle
            if (_splitDragging && _activeBuilding != null)
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
                {
                    if (_activeBuilding.TryGetWorldRect(out var dragRect))
                    {
                        // Map cursor Y to [0..1] within building rect, clamp [0.01..0.99]
                        float rawRatio = 1f - Mathf.Clamp01((worldPos.y - dragRect.yMin) / dragRect.height);
                        float newRatio = Mathf.Clamp(rawRatio, 0.01f, 0.99f);
                        _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, newRatio);
                        MarkInstanceDataDirty();
                        RefreshInspector();
                        if (_statusTmp != null)
                            _statusTmp.text = $"Split ratio â†’ {newRatio:F3}";
                    }
                }
                else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
                {
                    float finalRatio = _activeBuilding.SplitRatioOverride;
                    float startRatio = _splitDragStartRatio;
                    // Register as undoable action only if ratio actually changed
                    if (!Mathf.Approximately(finalRatio, startRatio))
                    {
                        ExecutePersistedEdit($"Split {finalRatio:F3}",
                            () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, finalRatio),
                            () => _activeBuilding.Apply(_activeBuilding.Template, _activeBuilding.ScaleOverride, startRatio));
                    }
                    _splitDragging = false;
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Split ratio set to {finalRatio:F3}.";
                }
                return;
            }

            // R-handle PointerDown sets _pendingResizeStart; we consume it here so
            // _resizeStartMouse is recorded at the world position for this frame.
            if (_pendingResizeStart && _activeBuilding != null)
            {
                _pendingResizeStart = false;
                _resizing         = true;
                _resizeStartMouse = worldPos;
                _resizeStartScale = (_activeBuilding.ScaleOverride.x > 0)
                    ? _activeBuilding.ScaleOverride
                    : (_activeBuilding.Template != null
                        ? _activeBuilding.Template.originalScale
                        : Vector2Int.one * 64);
                if (_statusTmp != null) _statusTmp.text = "Resize: drag to scale (proportional).";
            }

            // Resize drag â€” driven by LMB while _resizing is set by the R handle.
            if (_resizing && _activeBuilding != null)
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
                {
                    var delta = (Vector2)(worldPos - _resizeStartMouse);
                    // Preserve aspect ratio: dominant axis (|dx| vs |dy|) drives scale.
                    float aspect      = (float)_resizeStartScale.x / Mathf.Max(1, _resizeStartScale.y);
                    float signedDelta = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x : delta.y;
                    float pixDelta    = signedDelta * 32f;   // 32 px per world unit (building PPU)
                    int newW = Mathf.Max(8, _resizeStartScale.x + Mathf.RoundToInt(pixDelta));
                    int newH = Mathf.Max(8, Mathf.RoundToInt(newW / aspect));
                    _activeBuilding.Apply(_activeBuilding.Template, new Vector2Int(newW, newH), _activeBuilding.SplitRatioOverride);
                    MarkInstanceDataDirty();
                    if (_statusTmp != null) _statusTmp.text = $"Resize â†’ {newW}Ã—{newH} px (ratio {aspect:F2})";
                    RefreshInspector();
                }
                else if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
                {
                    FinalizeResizeDrag();
                }
                return;
            }

            // Move drag
            if (_dragging && _activeBuilding != null)
            {
                _activeBuilding.transform.position = worldPos + _dragOffset;
                MarkInstanceDataDirty();
                if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonReleasedThisFrame()) FinalizeMoveDrag();
                return;
            }

            if (overUi) return;

            // Collider painting â€” when a brush mode is active, LMB hold paints/erases
            // collider tiles on the active building. Returns early so it doesn't
            // interfere with selection/placement.
            if (_collBrushMode != CollBrushMode.Off && _activeBuilding != null
                && (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed() || Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame()))
            {
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    BeginColliderStroke();
                HandleColliderPaint(worldPos);
                return;
            }

            // LMB on split handle â€” start split-ratio drag
            if (!overUi && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame() && _activeBuilding != null
                && _activeBuilding.TryGetWorldRect(out var checkRect))
            {
                float sr = _activeBuilding.SplitRatioOverride >= 0f
                    ? _activeBuilding.SplitRatioOverride
                    : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);
                float handleWorldY = checkRect.yMin + checkRect.height * (1f - sr);
                float distY = Mathf.Abs(worldPos.y - handleWorldY);
                // Also check horizontal proximity (within building X bounds + small margin)
                float marginX = SPLIT_HANDLE_WORLD_RADIUS;
                bool withinX = worldPos.x >= checkRect.xMin - marginX && worldPos.x <= checkRect.xMax + marginX;
                if (distY <= SPLIT_HANDLE_WORLD_RADIUS && withinX)
                {
                    _splitDragging = true;
                    _splitDragStartRatio = sr;
                    return;   // consume event
                }
            }

            // LMB â€” primary action
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
            {
                if (_removeMode || _mode == EditorMode.Delete)
                {
                    if (_hoveredBuilding != null) RequestDeleteWithConfirm(_hoveredBuilding);
                    return;
                }
                // Click-to-place was removed: placement is drag-only (drag a
                // thumbnail from the Buildings panel onto the map). A bare LMB
                // click on the map only ever selects the hovered building.
                if (_hoveredBuilding != null) SetActiveBuilding(_hoveredBuilding);
            }

            // RMB on a building â†’ move drag (resize is now LMB-drag via the R handle).
            if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonPressedThisFrame() && _hoveredBuilding != null)
            {
                SetActiveBuilding(_hoveredBuilding);
                _dragging   = true;
                _dragStartWorldPos = _activeBuilding.transform.position;
                _dragOffset = _activeBuilding.transform.position - worldPos;
            }
        }

        private void FinalizeMoveDrag()
        {
            if (_activeBuilding == null)
            {
                _dragging = false;
                return;
            }

            _dragging = false;
            var building = _activeBuilding;
            Vector3 startPos = _dragStartWorldPos;
            Vector3 finalPos = building.transform.position;

            if ((finalPos - startPos).sqrMagnitude <= 0.0001f)
            {
                return;
            }

            ExecutePersistedEdit($"Move {building.InstanceId}",
                () =>
                {
                    if (building == null) return;
                    building.transform.position = finalPos;
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Move saved â†’ ({finalPos.x:F2}, {finalPos.y:F2})";
                },
                () =>
                {
                    if (building == null) return;
                    building.transform.position = startPos;
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Move reverted â†’ ({startPos.x:F2}, {startPos.y:F2})";
                });
        }

        private void FinalizeResizeDrag()
        {
            if (_activeBuilding == null)
            {
                _resizing = false;
                return;
            }

            _resizing = false;
            var building = _activeBuilding;
            Vector2Int startScale = _resizeStartScale;
            Vector2Int finalScale = building.ScaleOverride;

            if (finalScale == startScale)
            {
                RefreshCollisionFor(building);
                RefreshInspector();
                return;
            }

            ExecutePersistedEdit($"Resize {finalScale.x}x{finalScale.y}",
                () =>
                {
                    if (building == null) return;
                    building.Apply(building.Template, finalScale, building.SplitRatioOverride);
                    RefreshCollisionFor(building);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = "Resize saved.";
                },
                () =>
                {
                    if (building == null) return;
                    building.Apply(building.Template, startScale, building.SplitRatioOverride);
                    RefreshCollisionFor(building);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = "Resize reverted.";
                });
        }

        private void RecomputeHoverStack(Vector3 worldPos)
        {
            _hoverStack.Clear();
            // OverlapPointAll returns colliders whose footprint contains worldPos.
            // Buildings only have a collider over the FOOTPRINT (below split). To
            // also catch the canopy region we test the full sprite rect explicitly.
            var all = FindObjectsOfType<BuildingObject>();
            for (int i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || !b.TryGetWorldRect(out var r)) continue;
                if (r.Contains(worldPos)) _hoverStack.Add(b);
            }
            if (_hoverStack.Count == 0) { _hoveredBuilding = null; return; }
            // Stable sort: prefer the visually-front-most (highest Y baseline = lower in world)
            _hoverStack.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            if (_hoverIndex >= _hoverStack.Count) _hoverIndex = 0;
            _hoveredBuilding = _hoverStack[_hoverIndex];
        }

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        //  ACTIVE BUILDING + INSPECTOR
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void SetActiveBuilding(BuildingObject b)
        {
            bool changed = _activeBuilding != b;
            _activeBuilding = b;
            // Clicking a placed instance always switches back to Instance mode,
            // regardless of whether the picker had previously entered Template mode.
            _propertiesMode = b != null ? PropertiesMode.Instance : PropertiesMode.None;
            // Drop the cached session so the next paint refreshes it for the new
            // building, and refresh the overlay so the OLD active building reverts
            // to BoxCollider2D rendering and the NEW one (if any) gets authoring
            // cells pushed in.
            if (changed) _activeColliderSession = null;
            if (changed) RebuildSameTemplateFx(b);
            RefreshInspector();
            if (_collidersVisible) RefreshCollidersOverlay();
            if (_statusTmp != null && b != null) _statusTmp.text = $"Active: ID {b.InstanceId} ({b.Template?.name})";
        }

    }
}
