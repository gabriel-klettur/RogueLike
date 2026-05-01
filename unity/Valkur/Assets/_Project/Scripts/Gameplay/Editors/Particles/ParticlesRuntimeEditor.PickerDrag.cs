using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.VFX
{
    /// <summary>
    /// Drag-from-picker spawn for the Particles Editor (F1).
    /// Mirrors EntitiesRuntimeEditor.PickerDrag: LMB-pressing a picker slot starts
    /// a drag once the cursor moves past <see cref="PICKER_DRAG_THRESHOLD"/> px;
    /// releasing over the map spawns an emitter at the world position under the
    /// cursor. A floating UI ghost (canvas overlay) stays above panels and HUD.
    /// </summary>
    public partial class ParticlesRuntimeEditor : SingletonMonoBehaviour<ParticlesRuntimeEditor>, GameEditorManager.IGameEditor
    {
        // â”€â”€ Drag state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private bool    _pickerDragging;
        private string  _pickerDragPresetId;
        private Vector2 _pickerDragStartScreen;
        private const float PICKER_DRAG_THRESHOLD = 8f;

        // Drag preview (Canvas Overlay â†’ renders above world AND every panel).
        private GameObject    _dragGhostGo;
        private RectTransform _dragGhostRt;
        private Image         _dragGhostImg;
        private Image         _dragGhostOutline;

        private const float DRAG_GHOST_BORDER  = 10f;
        private const float DRAG_GHOST_DEFAULT = 64f;
        private static readonly Color DRAG_GHOST_TINT    = new Color(0.55f, 1f, 1f, 0.70f);
        private static readonly Color DRAG_GHOST_OUTLINE = new Color(1f, 0.85f, 0.10f, 0.95f);

        private Camera _cachedMainCamera;
        private Camera CachedMainCamera => _cachedMainCamera != null
            ? _cachedMainCamera
            : (_cachedMainCamera = Camera.main);

        // ── Picker slot pointer-down (registered by Picker.AddPickerSlot) ──────
        private void OnPickerSlotPointerDown(string presetId)
        {
            _pickerDragPresetId    = presetId;
            // MouseInputManager wraps the OR-of-new-and-legacy fallback so the
            // start screen position survives an InputSystem-drops-events frame.
            _pickerDragStartScreen = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
        }

        // â”€â”€ Ghost construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void BuildDragGhost()
        {
            if (_dragGhostGo != null) return;

            _dragGhostGo = EditorUIHelpers.CreateUI("PickerDragGhost", _canvas.transform);
            _dragGhostRt = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(DRAG_GHOST_DEFAULT, DRAG_GHOST_DEFAULT);
            _dragGhostRt.anchorMin = _dragGhostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _dragGhostRt.pivot     = new Vector2(0.5f, 0.5f);

            // Outline (renders behind body)
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-DRAG_GHOST_BORDER, -DRAG_GHOST_BORDER);
            outlineRt.offsetMax = new Vector2( DRAG_GHOST_BORDER,  DRAG_GHOST_BORDER);
            _dragGhostOutline = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            // Body (filled disc â€” particles have no fixed sprite, so we use a solid tint).
            var bodyGo = EditorUIHelpers.CreateUI("Body", _dragGhostGo.transform);
            var bodyRt = bodyGo.GetComponent<RectTransform>();
            bodyRt.anchorMin = Vector2.zero;
            bodyRt.anchorMax = Vector2.one;
            bodyRt.offsetMin = bodyRt.offsetMax = Vector2.zero;
            _dragGhostImg                = bodyGo.AddComponent<Image>();
            _dragGhostImg.color          = DRAG_GHOST_TINT;
            _dragGhostImg.raycastTarget  = false;

            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        // â”€â”€ Per-frame drag update â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void UpdatePickerDrag()
        {
            // Don't bail when Mouse.current is null — MouseInputManager has a
            // legacy backend fallback that keeps reading.
            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();

            // Phase 1: waiting for drag threshold.
            if (!_pickerDragging && !string.IsNullOrEmpty(_pickerDragPresetId))
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        _pickerDragging = true;
                        BuildDragGhost();
                        _dragGhostGo.transform.SetAsLastSibling();
                        _dragGhostGo.SetActive(true);
                        SetStatus($"Dragging '{_pickerDragPresetId}' â€” release over the map to spawn.");
                    }
                }
                else
                {
                    // Released before threshold â†’ normal click handled by Button.onClick.
                    _pickerDragPresetId = null;
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2: ghost follows the cursor.
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : CachedMainCamera,
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Pulsating outline (matches Entities feel).
            if (_dragGhostOutline != null)
            {
                float t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 5f) + 1f) * 0.5f;
                var c = DRAG_GHOST_OUTLINE;
                c.a = Mathf.Lerp(0.35f, 1.0f, t);
                _dragGhostOutline.color = c;
            }

            // Drop.
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                var cam = CachedMainCamera;
                if (!overUi && cam != null)
                {
                    Vector3 worldPos = cam.ScreenToWorldPoint(screenPos);
                    worldPos.z = 0f;
                    SpawnFromPickerDrag(_pickerDragPresetId, worldPos);
                }
                else
                {
                    SetStatus("Drag cancelled (released over UI).");
                }
                CancelPickerDrag();
            }
        }

        private void CancelPickerDrag()
        {
            _pickerDragging      = false;
            _pickerDragPresetId  = null;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }

        private void SpawnFromPickerDrag(string presetId, Vector3 worldPos)
        {
            if (string.IsNullOrEmpty(presetId) || _catalog == null) return;
            // Make this preset the current selection so the editor reflects it.
            if (_selectedPresetId != presetId) SelectPreset(presetId);
            SpawnFromMapClick(presetId, worldPos);
        }
    }
}
