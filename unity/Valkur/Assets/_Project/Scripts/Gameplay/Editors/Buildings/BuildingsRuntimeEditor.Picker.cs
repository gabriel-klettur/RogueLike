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

        /// <summary>
        /// Re-filters the picker and re-realises its visible window. Cheap now — a filter
        /// pass over the catalog plus at most a few dozen pooled slots — so it is safe to
        /// call from open, restore, search and tab change. Selection alone goes through
        /// <see cref="RefreshPickerSelection"/> and never comes here.
        /// See BuildingsRuntimeEditor.Picker.Virtual.cs for the window itself.
        /// </summary>
        private void RefreshPicker()
        {
            if (_pickerContent == null) return;
            PreparePickerVirtualization();

            BuildPickerVisibleList();
            int shown = _pickerVisible.Count;

            // Columns and cell size come from the live width, so a resized panel — or one
            // whose width the workspace just restored — lays out against what it actually is.
            RecomputePickerMetrics();
            ApplyPickerContentSize();

            RecycleAllPickerSlots();
            _pickerFirst = _pickerLast = -1;
            UpdatePickerVisibleSlots();

            if (_statusTmp != null)
            {
                string filter = _searchFilter?.Trim() ?? "";
                string scope = IsCategoryFilterActive
                    ? $" in {BuildingCategory.Label(ActiveCategory)}"
                    : "";
                _statusTmp.text = filter.Length == 0
                    ? $"{shown} templates{scope}"
                    : $"{shown} match '{_searchFilter}'{scope}";
            }
        }

        /// <summary>
        /// Active category tab, meaningful only when <see cref="IsCategoryFilterActive"/>.
        /// </summary>
        private BuildingCategory.Category ActiveCategory
            => System.Enum.TryParse(_categoryFilter, out BuildingCategory.Category c)
                ? c
                : BuildingCategory.Category.Structures;

        /// <summary>False on the "All" tab and before any tab has been chosen.</summary>
        private bool IsCategoryFilterActive
            => !string.IsNullOrEmpty(_categoryFilter)
               && _categoryFilter != BuildingsEditorUIBuilder.CATEGORY_ALL_KEY
               && System.Enum.TryParse(_categoryFilter, out BuildingCategory.Category _);

        /// <summary>
        /// Category gate for the picker grid. Kept separate from the search gate so the two
        /// compose: a query narrows whatever the active tab already shows.
        /// </summary>
        private bool MatchesCategoryFilter(BuildingTemplateData template)
            => !IsCategoryFilterActive || BuildingCategory.Of(template) == ActiveCategory;

        private void SelectTemplate(int id)
        {
            // Fill flow intercept: if we are waiting for a template selection,
            // route to OnFillTemplatePicked and return early so the normal
            // Properties/picker path doesn't run.
            if (_fillStep == FillStep.AwaitingTemplate)
            {
                OnFillTemplatePicked(id);
                return;
            }

            _selectedTemplateId = id;

            // Entering Template mode: clear the placed-instance selection so the
            // Properties panel shows template info instead of the last active building.
            if (_activeBuilding != null)
            {
                _activeBuilding = null;
                _activeColliderSession = null;
                RebuildSameTemplateFx(null);
                if (_activeFx != null) { _activeFx.Follow(null); _activeFx.SetVisible(false); }
                // Hide world-space overlays that only make sense for an instance.
                if (_handlesRoot   != null) _handlesRoot.SetActive(false);
                if (_idLabelRt     != null) _idLabelRt.gameObject.SetActive(false);
                if (_splitLineRt   != null) _splitLineRt.gameObject.SetActive(false);
                if (_splitHandleRt != null) _splitHandleRt.gameObject.SetActive(false);
                if (_collidersVisible) RefreshCollidersOverlay();
            }

            _propertiesMode = PropertiesMode.Template;
            RefreshPickerSelection();
            RefreshInspector();

            // Placement is drag-only: do NOT auto-switch to Place mode. The user
            // must drag the slot from the picker onto the map to actually place a
            // building. A simple click only highlights the slot for inspection.
            if (_statusTmp != null)
                _statusTmp.text = $"Template #{id} highlighted. DRAG it from the panel onto the map to place.";
        }

        // ── Drag-from-picker ─────────────────────────────────────────────────────
        // Mirrors Python building_picker_controller.start_drag / place_building and
        // building_picker_view._draw_drag_preview.

        /// <summary>
        /// Creates the picker drag preview — a vivid-colored UI Image rendered on the
        /// editor's Canvas Overlay so it floats above the world AND any UI panels.
        /// Always rendered as the topmost sibling of the canvas so panels can't occlude it.
        ///
        /// Hierarchy (Canvas render order: parent first → children in order):
        ///   PickerDragGhost (container, no Image — anchor 0.5/0.5 for correct cursor mapping)
        ///     Outline  (Image — extends DRAG_GHOST_BORDER px outward, renders BEHIND sprite)
        ///     Sprite   (Image — fills ghost rect exactly, renders ON TOP of outline)
        /// </summary>
        private void BuildDragGhost()
        {
            if (_dragGhostGo != null) return;

            // Container — no Image component on this node.
            // Anchor at 0.5/0.5 so ScreenPointToLocalPointInRectangle output maps
            // directly to anchoredPosition without a canvas-center offset.
            _dragGhostGo = EditorUIHelpers.CreateUI("PickerDragGhost", _canvas.transform);
            _dragGhostRt = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(80f, 80f);
            _dragGhostRt.anchorMin = _dragGhostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _dragGhostRt.pivot     = new Vector2(0.5f, 0.5f);

            // Child 1 — outline border (renders first = behind the sprite).
            // Extends DRAG_GHOST_BORDER px outside the ghost rect on all sides.
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-DRAG_GHOST_BORDER, -DRAG_GHOST_BORDER);
            outlineRt.offsetMax = new Vector2( DRAG_GHOST_BORDER,  DRAG_GHOST_BORDER);
            _dragGhostOutline               = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            // Child 2 — building sprite (renders second = on top of outline).
            // Fills the ghost rect exactly while preserving sprite aspect so the
            // preview never stretches if a source asset has unusual dimensions.
            var spriteGo = EditorUIHelpers.CreateUI("Sprite", _dragGhostGo.transform);
            var spriteRt = spriteGo.GetComponent<RectTransform>();
            spriteRt.anchorMin = Vector2.zero;
            spriteRt.anchorMax = Vector2.one;
            spriteRt.offsetMin = spriteRt.offsetMax = Vector2.zero;
            _dragGhostImg               = spriteGo.AddComponent<Image>();
            _dragGhostImg.raycastTarget = false;
            _dragGhostImg.preserveAspect = true;
            _dragGhostImg.color         = DRAG_GHOST_TINT;

            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            // Force topmost so panels/menus can't render on top of the preview.
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        /// <summary>
        /// Sizes the drag-ghost RectTransform so its on-screen pixel size matches the
        /// building's actual world footprint at the current camera zoom. Returns true
        /// when the size could be computed; falls back to the default 80×80 otherwise.
        /// </summary>
        private void SizeDragGhostToWorldFootprint(BuildingTemplateData tmpl)
        {
            if (tmpl == null || _dragGhostRt == null) return;
            float worldW = Mathf.Max(0.01f, tmpl.originalScale.x / BUILDING_PPU);
            float worldH = Mathf.Max(0.01f, tmpl.originalScale.y / BUILDING_PPU);

            float pxPerWorldUnit = 32f; // safe default
            if (_mainCamera != null && _mainCamera.orthographic && _mainCamera.orthographicSize > 0.001f)
                pxPerWorldUnit = Screen.height / (2f * _mainCamera.orthographicSize);

            float scaleFactor = (_canvas != null && _canvas.scaleFactor > 0.001f) ? _canvas.scaleFactor : 1f;
            float wPx = worldW * pxPerWorldUnit / scaleFactor;
            float hPx = worldH * pxPerWorldUnit / scaleFactor;
            // Clamp so absurdly large buildings (e.g. catedrals) don't fill the entire screen.
            const float MAX_PX = 512f;
            if (wPx > MAX_PX || hPx > MAX_PX)
            {
                float k = MAX_PX / Mathf.Max(wPx, hPx);
                wPx *= k; hPx *= k;
            }
            _dragGhostRt.sizeDelta = new Vector2(wPx, hPx);
        }

        /// <summary>Called from each slot's EventTrigger.PointerDown — records drag origin.</summary>
        private void OnPickerSlotPointerDown(int templateId)
        {
            _pickerDragTemplateId  = templateId;
            // Read through MouseInputManager so the legacy backend supplies the
            // start position when the new InputSystem package is dropping OS
            // events (recurring Unity 2022.3 Editor bug — see MouseInputManager).
            _pickerDragStartScreen = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
        }

        /// <summary>
        /// Activates the ghost once the drag threshold is crossed, moves it with the
        /// cursor, and on LMB release over the map places the building.
        /// </summary>
        private void UpdatePickerDrag()
        {
            // Cancel the pending picker candidate if the user released the mouse
            // before the drag threshold was crossed. Routed through MouseInputManager
            // so a temporarily-broken new InputSystem doesn't falsely report
            // "released" (the legacy backend keeps the real held state).
            if (!_pickerDragging && _pickerDragTemplateId >= 0
                && !Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
            {
                _pickerDragTemplateId = -1;
                return;
            }

            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();

            // Phase 1 — waiting for drag threshold
            if (!_pickerDragging && _pickerDragTemplateId >= 0)
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        var tmpl = _catalog?.GetById(_pickerDragTemplateId);
                        if (tmpl != null)
                        {
                            _pickerDragging     = true;
                            _selectedTemplateId = _pickerDragTemplateId;
                            RefreshPickerSelection();
                            BuildDragGhost();
                            _dragGhostImg.sprite  = tmpl.previewSprite;
                            _dragGhostImg.enabled = tmpl.previewSprite != null;
                            _dragGhostImg.color   = DRAG_GHOST_TINT;
                            // Size the on-screen ghost to match the building's real footprint.
                            SizeDragGhostToWorldFootprint(tmpl);
                            // Make sure the ghost stays above any panel that may have been
                            // re-parented or rebuilt since the editor was opened.
                            _dragGhostGo.transform.SetAsLastSibling();
                            _dragGhostGo.SetActive(true);

                            if (_statusTmp != null)
                                _statusTmp.text = $"Dragging template #{_pickerDragTemplateId} — release over the map to place.";
                        }
                    }
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2 — ghost follows the cursor on the canvas. Because the ghost lives
            // on the editor's Canvas Overlay AND is forced to the last sibling, it
            // renders above the world AND above every UI panel/menu in the scene.
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mainCamera,
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Blink the yellow border (5 Hz sine pulse, 0.35 → 1.0 alpha range).
            if (_dragGhostOutline != null)
            {
                float t = (Mathf.Sin(Time.time * Mathf.PI * 5f) + 1f) * 0.5f; // 0..1
                var c = DRAG_GHOST_OUTLINE;
                c.a = Mathf.Lerp(0.35f, 1.0f, t);
                _dragGhostOutline.color = c;
            }

            // Drop
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                if (!overUi && _mainCamera != null)
                {
                    Vector3 worldPos = _mainCamera.ScreenToWorldPoint(screenPos);
                    worldPos.z = 0f;
                    // BuildingObject pivot is bottom-center (sprites grow upward from Y=0).
                    // The ghost pivot is center (0.5, 0.5), so the cursor sits at the visual
                    // center of the preview. Without correction the building's bottom lands at
                    // the cursor and the whole sprite appears shifted up by halfHeight.
                    // → Shift worldPos down by half the building's world height so the visual
                    //   center of the placed building matches where the ghost was shown.
                    var dropTmpl = _catalog?.GetById(_pickerDragTemplateId);
                    if (dropTmpl != null && dropTmpl.originalScale.y > 0)
                        worldPos.y -= (dropTmpl.originalScale.y / BUILDING_PPU) * 0.5f;
                    // Drag-only placement: PlaceBuilding() spawns at the drop
                    // position regardless of current EditorMode. We do NOT mutate
                    // _mode here so the user stays in Select after placing.
                    PlaceBuilding(worldPos);
                }
                else if (_statusTmp != null)
                {
                    _statusTmp.text = "Drag cancelled (released over UI). Drop on the map to place.";
                }
                CancelPickerDrag();
            }
        }

        /// <summary>Hides the ghost and resets all drag-from-picker state.</summary>
        private void CancelPickerDrag()
        {
            _pickerDragging       = false;
            _pickerDragTemplateId = -1;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }

    }
}
