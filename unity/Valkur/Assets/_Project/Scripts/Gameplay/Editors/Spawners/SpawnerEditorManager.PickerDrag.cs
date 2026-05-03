using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Core.Input;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Spawners
{
    /// <summary>
    /// Drag-from-picker spawn for the Spawner Editor (F3).
    /// Mirrors <see cref="Valkur.Gameplay.Entities.EntitiesRuntimeEditor"/> behaviour:
    /// LMB-pressing a picker row arms a drag; once the cursor moves past
    /// <see cref="PICKER_DRAG_THRESHOLD"/> pixels, a floating UI ghost follows the
    /// pointer and releasing over the map calls <c>PlaceSpawner</c> at the world
    /// position. Spawners have no sprite of their own so the ghost is a tinted
    /// circle sized to the template's <c>triggerRadius</c>, which doubles as a
    /// preview of the spawner's effective range.
    /// </summary>
    public partial class SpawnerEditorManager
        : SingletonMonoBehaviour<SpawnerEditorManager>, GameEditorManager.IGameEditor
    {
        // ── Drag-from-picker state ────────────────────────────────────────────

        private bool                _pickerDragging;
        private SpawnerTemplateData _pickerDragTemplate;
        private Vector2             _pickerDragStartScreen;

        private const float PICKER_DRAG_THRESHOLD = 8f; // px before click→drag

        // Floating UI ghost on the editor canvas (overlays the world AND panels).
        private GameObject    _dragGhostGo;
        private RectTransform _dragGhostRt;
        private Image         _dragGhostFill;
        private Image         _dragGhostOutline;

        private const float DRAG_GHOST_BORDER  = 6f;   // px outline thickness
        private const float DRAG_GHOST_MIN_PX  = 32f;
        private const float DRAG_GHOST_MAX_PX  = 256f;
        private const float DRAG_GHOST_DEFAULT = 64f;  // px fallback when radius == 0

        private static readonly Color DRAG_GHOST_FILL    = new Color(1f, 0.65f, 0.20f, 0.22f);
        private static readonly Color DRAG_GHOST_OUTLINE = new Color(1f, 0.65f, 0.20f, 0.95f);

        // ── Slot pointer-down (registered in BuildPickerRow via EventTrigger) ─

        private void OnPickerSlotPointerDown(SpawnerTemplateData template)
        {
            if (template == null) return;
            _pickerDragTemplate    = template;
            _pickerDragStartScreen = MouseInputManager.GetScreenMousePosition();
        }

        // ── Ghost construction ────────────────────────────────────────────────

        private void BuildDragGhost()
        {
            if (_dragGhostGo != null || _canvas == null) return;

            _dragGhostGo = EditorUIHelpers.CreateUI("PickerDragGhost", _canvas.transform);
            _dragGhostRt = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(DRAG_GHOST_DEFAULT, DRAG_GHOST_DEFAULT);
            _dragGhostRt.anchorMin = _dragGhostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _dragGhostRt.pivot     = new Vector2(0.5f, 0.5f);

            // Outline (renders behind the fill)
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-DRAG_GHOST_BORDER, -DRAG_GHOST_BORDER);
            outlineRt.offsetMax = new Vector2( DRAG_GHOST_BORDER,  DRAG_GHOST_BORDER);
            _dragGhostOutline               = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            // Translucent fill (renders on top of outline)
            var fillGo = EditorUIHelpers.CreateUI("Fill", _dragGhostGo.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            _dragGhostFill                = fillGo.AddComponent<Image>();
            _dragGhostFill.color          = DRAG_GHOST_FILL;
            _dragGhostFill.raycastTarget  = false;

            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        /// <summary>
        /// Sizes the ghost so its on-screen pixel diameter matches the template's
        /// trigger radius at the current camera zoom — this gives the user a live
        /// preview of where the spawner's effective range will fall.
        /// </summary>
        private void SizeDragGhostFromTemplate(SpawnerTemplateData template)
        {
            if (_dragGhostRt == null) return;

            float worldDiameter = (template != null && template.triggerRadius > 0f)
                ? template.triggerRadius * 2f
                : 0f;

            float pxPerWorldUnit = 32f;
            var cam = _camera != null ? _camera : Camera.main;
            if (cam != null && cam.orthographic && cam.orthographicSize > 0.001f)
                pxPerWorldUnit = Screen.height / (2f * cam.orthographicSize);

            float canvasScale = (_canvas != null && _canvas.scaleFactor > 0.001f) ? _canvas.scaleFactor : 1f;

            float sizePx = worldDiameter > 0f
                ? worldDiameter * pxPerWorldUnit / canvasScale
                : DRAG_GHOST_DEFAULT;
            sizePx = Mathf.Clamp(sizePx, DRAG_GHOST_MIN_PX, DRAG_GHOST_MAX_PX);
            _dragGhostRt.sizeDelta = new Vector2(sizePx, sizePx);
        }

        // ── Per-frame drag update (called from Update while editor is active) ─

        private void UpdatePickerDrag()
        {
            // Route through MouseInputManager so the legacy backend takes over
            // when the new InputSystem package drops events (Unity 2022.3 bug).
            Vector2 screenPos          = MouseInputManager.GetScreenMousePosition();
            bool    leftPressed        = MouseInputManager.IsLeftMouseButtonPressed();
            bool    leftReleasedFrame  = MouseInputManager.WasLeftMouseButtonReleasedThisFrame();

            // Phase 1 — armed but waiting for the drag threshold.
            if (!_pickerDragging && _pickerDragTemplate != null)
            {
                if (leftPressed)
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        _pickerDragging = true;
                        BuildDragGhost();
                        SizeDragGhostFromTemplate(_pickerDragTemplate);
                        _dragGhostGo.transform.SetAsLastSibling();
                        _dragGhostGo.SetActive(true);

                        SetStatus($"Dragging '{_pickerDragTemplate.templateId}' — release over the map to place.");
                    }
                }
                else
                {
                    // Released before threshold → normal click handled by Button.onClick.
                    _pickerDragTemplate = null;
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2 — ghost follows the cursor.
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : (_camera != null ? _camera : Camera.main),
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Pulsating outline.
            if (_dragGhostOutline != null)
            {
                float t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 5f) + 1f) * 0.5f;
                var c = DRAG_GHOST_OUTLINE;
                c.a = Mathf.Lerp(0.35f, 1.0f, t);
                _dragGhostOutline.color = c;
            }

            // Drop.
            if (leftReleasedFrame)
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                var cam = _camera != null ? _camera : Camera.main;
                if (!overUi && cam != null)
                {
                    Vector3 worldPos = cam.ScreenToWorldPoint(screenPos);
                    worldPos.z = 0f;
                    PlaceSpawner(_pickerDragTemplate, worldPos);
                }
                else
                {
                    SetStatus("Drag cancelled (released over UI). Drop on the map to place.");
                }
                CancelPickerDrag();
            }
        }

        private void CancelPickerDrag()
        {
            _pickerDragging     = false;
            _pickerDragTemplate = null;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }
    }
}
