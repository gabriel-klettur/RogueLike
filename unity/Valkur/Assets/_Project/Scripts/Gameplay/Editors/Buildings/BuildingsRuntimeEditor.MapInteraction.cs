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
            _splitHovering = !overUi && IsScreenPosOnSplitHandle(_activeBuilding, cam, screenPos);

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

                // Lock aspect against the building's CURRENT visible bounds, not
                // Template.originalScale. The Unity SpriteAtlas trims transparent
                // borders even with rectangular packing, so a logical 1024×1024
                // PNG can land in the atlas at 552×961. If we used originalScale
                // here (aspect = 1.0), every resize would force the new override
                // to match logical-square — but Apply() scales the trimmed atlas
                // sprite by effW/atlasW vs effH/atlasH, which becomes non-uniform
                // and squishes the visible art horizontally on every drag. The
                // visible world rect already reflects the actual rendered size,
                // so deriving aspect from it locks the resize to whatever the
                // user is seeing on screen.
                _resizeStartScale = TryGetVisibleBoundsAsPixelSize(_activeBuilding, out var visibleSize)
                    ? visibleSize
                    : (_activeBuilding.ScaleOverride.x > 0
                        ? _activeBuilding.ScaleOverride
                        : (_activeBuilding.Template != null
                            ? _activeBuilding.Template.originalScale
                            : Vector2Int.one * 64));
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
                // Y changes during the drag → re-sort the bottom + top
                // renderers against their new world Y, otherwise the
                // building keeps the sortingOrder it had when the drag
                // started and visibly clips on top of (or behind) entities
                // it has moved past.
                _activeBuilding.RefreshSorting();
                MarkInstanceDataDirty();
                if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonReleasedThisFrame()) FinalizeMoveDrag();
                return;
            }

            if (overUi) return;

            // Door authoring - click picks the building whose doorway the flyout edits.
            // Placed BEFORE the collider brush because that branch returns early whenever a
            // brush is left switched on, which would otherwise swallow every Door-mode click.
            if (_mode == EditorMode.Door)
            {
                if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame())
                    HandleDoorModeClick(worldPos);
                return;
            }

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

            // LMB on split handle â€” start split-ratio drag. Hit test is
            // SCREEN-space (IsScreenPosOnSplitHandle) so the interactive band
            // stays a fixed, generous size in pixels regardless of camera zoom.
            if (!overUi && Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame() && _activeBuilding != null
                && IsScreenPosOnSplitHandle(_activeBuilding, cam, screenPos))
            {
                float sr = _activeBuilding.SplitRatioOverride >= 0f
                    ? _activeBuilding.SplitRatioOverride
                    : (_activeBuilding.Template != null ? _activeBuilding.Template.splitRatio : 0.5f);
                _splitDragging = true;
                _splitDragStartRatio = sr;
                return;   // consume event
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

            // RMB drag-moves the ALREADY-SELECTED building (resize is LMB-drag
            // via the R handle). Selection is LEFT-click only (see the LMB
            // primary-action block above) — a bare RMB press on a building
            // that isn't the current selection does nothing, freeing the
            // button for future context-menu actions instead of doubling as
            // an implicit select.
            if (Valkur.Core.Input.MouseInputManager.WasRightMouseButtonPressedThisFrame()
                && _hoveredBuilding != null && _hoveredBuilding == _activeBuilding)
            {
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
                    building.RefreshSorting();
                    // The doorway is a child, so it moved with the building - but its rect is
                    // derived from the building's world bounds, so the object has to be
                    // re-placed on it. Same contract RefreshSorting carries for the Y-sort.
                    BuildingDoorFactory.RefreshGeometry(building);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Move saved â†’ ({finalPos.x:F2}, {finalPos.y:F2})";
                },
                () =>
                {
                    if (building == null) return;
                    building.transform.position = startPos;
                    building.RefreshSorting();
                    BuildingDoorFactory.RefreshGeometry(building);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = $"Move reverted â†’ ({startPos.x:F2}, {startPos.y:F2})";
                });
        }

        /// <summary>
        /// Whether <paramref name="screenPos"/> is within the padded, fixed-size
        /// interactive band around <paramref name="building"/>'s split-ratio bar
        /// (the on-screen line drawn at the Z-layer boundary between the
        /// Footprint/WallsBottom and Canopy/WallsTop halves — see UpdateSplitLine).
        /// Projects the same world-space split Y that draws the visual bar into
        /// SCREEN space and pads by a fixed pixel amount, so the hit area tracks
        /// the visible bar exactly and never shrinks below a usable size when the
        /// editor camera is zoomed out. Used by both the hover flag and the LMB
        /// click that starts a split-ratio drag.
        /// </summary>
        private static bool IsScreenPosOnSplitHandle(BuildingObject building, Camera cam, Vector2 screenPos)
        {
            if (building == null || cam == null || !building.TryGetWorldRect(out var rect)) return false;

            float sr = building.SplitRatioOverride >= 0f
                ? building.SplitRatioOverride
                : (building.Template != null ? building.Template.splitRatio : 0.5f);
            float worldSplitY = rect.yMin + rect.height * (1f - sr);

            Vector3 leftScreen  = cam.WorldToScreenPoint(new Vector3(rect.xMin, worldSplitY, 0f));
            Vector3 rightScreen = cam.WorldToScreenPoint(new Vector3(rect.xMax, worldSplitY, 0f));

            float minX = Mathf.Min(leftScreen.x, rightScreen.x) - SPLIT_HANDLE_SCREEN_PADDING_PX;
            float maxX = Mathf.Max(leftScreen.x, rightScreen.x) + SPLIT_HANDLE_SCREEN_PADDING_PX;
            float lineScreenY = leftScreen.y; // same world Y on both sides → same screen Y

            return screenPos.x >= minX && screenPos.x <= maxX
                && Mathf.Abs(screenPos.y - lineScreenY) <= SPLIT_HANDLE_SCREEN_PADDING_PX;
        }

        // Returns the building's current visible bounds in PIXEL units (world units * PPU).
        // Falls back to false when the renderers haven't materialised yet (e.g. the
        // sprite failed to load), so the caller can use a sensible default.
        private static bool TryGetVisibleBoundsAsPixelSize(BuildingObject b, out Vector2Int sizeInPixels)
        {
            sizeInPixels = default;
            if (b == null) return false;
            if (!b.TryGetWorldRect(out var rect)) return false;
            const float BUILDING_PPU = 32f;
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width  * BUILDING_PPU));
            int h = Mathf.Max(1, Mathf.RoundToInt(rect.height * BUILDING_PPU));
            sizeInPixels = new Vector2Int(w, h);
            return true;
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
                    // A resize changes the bounds the doorway is a fraction OF, so its world
                    // rect moves even though nothing about the anchor did.
                    BuildingDoorFactory.RefreshGeometry(building);
                    RefreshInspector();
                    if (_statusTmp != null) _statusTmp.text = "Resize saved.";
                },
                () =>
                {
                    if (building == null) return;
                    building.Apply(building.Template, startScale, building.SplitRatioOverride);
                    RefreshCollisionFor(building);
                    BuildingDoorFactory.RefreshGeometry(building);
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
