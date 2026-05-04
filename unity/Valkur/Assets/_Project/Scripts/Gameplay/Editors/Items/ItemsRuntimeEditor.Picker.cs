using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Editors;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor â€” picker grid (left "Items" panel).
    /// Mirrors Python <c>roguelike_editors/items/ui/picker_view.py</c>:
    ///  â€¢ One slot per ItemDefinition, scaled icon + truncated label.
    ///  â€¢ LMB = select (drives Properties panel).
    ///  â€¢ RMB = spawn one at the player's current world position.
    ///  â€¢ Selection is highlighted with the SLOT_SELECTED color.
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        private static readonly Color PickerSlotNormal   = EditorUIHelpers.SLOT_BG;
        private static readonly Color PickerSlotSelected = EditorUIHelpers.SLOT_SELECTED;

        /// <summary>Rebuild the picker grid based on the current filter & selection.</summary>
        private void RefreshPicker()
        {
            if (_uiRefs.PickerContent == null) return;
            EnsureCatalog();
            ApplyFilter();

            // Clear children. Use DestroyImmediate when not in Play Mode so EditMode
            // tests see an accurate post-rebuild count (Object.Destroy is deferred).
            for (int i = _uiRefs.PickerContent.childCount - 1; i >= 0; i--)
            {
                var child = _uiRefs.PickerContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }

            for (int i = 0; i < _filtered.Count; i++)
            {
                var def  = _filtered[i];
                var capId = def.itemId;

                var (btn, icon, label) = EditorUIHelpers.MakeSlotButton(
                    _uiRefs.PickerContent, def.displayName ?? capId, 64f,
                    () => SelectItem(capId));

                if (def.icon != null)        { icon.sprite = def.icon;      icon.enabled = true; }
                else if (def.iconSmall != null){ icon.sprite = def.iconSmall; icon.enabled = true; }
                else if (def.iconLarge != null){ icon.sprite = def.iconLarge; icon.enabled = true; }

                label.text = TruncateName(def.displayName ?? capId, 9);

                if (capId == _selectedItemId)
                {
                    var img = btn.GetComponent<Image>();
                    if (img != null) img.color = PickerSlotSelected;
                }

                // Right-click â†’ spawn at player. We attach a custom event trigger because
                // UnityEngine.UI.Button does not natively distinguish LMB vs RMB.
                AddRightClickHandler(btn.gameObject, () => SpawnAtPlayer(capId));

                // Drag-from-picker (LMB hold + move): mirror BuildingsRuntimeEditor.
                // Records the drag origin; UpdatePickerDrag() promotes it into a ghost
                // once the cursor crosses PICKER_DRAG_THRESHOLD pixels and drops a
                // WorldPickup at the cursor's world position on LMB release.
                var et  = btn.gameObject.GetComponent<EventTrigger>() ?? btn.gameObject.AddComponent<EventTrigger>();
                var pde = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
                pde.callback.AddListener(_ => OnPickerSlotPointerDown(capId));
                et.triggers.Add(pde);
            }

            string filter = (_searchFilter ?? "").Trim();
            string status = filter.Length == 0
                ? $"{_filtered.Count} item(s) in catalog"
                : $"{_filtered.Count} match '{filter}'";
            SetStatus(status);
        }

        /// <summary>Select an item from the picker (drives Properties panel).
        /// Clears any previously-active world instance so the Properties panel
        /// shows catalog data only — instance metadata only appears once the
        /// user clicks an actual drop in the world or the instances list.</summary>
        private void SelectItem(string itemId)
        {
            _selectedItemId = itemId;
            _selectedInstance = null;
            RefreshPicker();
            RefreshProperties();
            RebuildInstancesList();
        }

        private static void AddRightClickHandler(GameObject go, System.Action onRightClick)
        {
            if (go == null) return;
            var trigger = go.GetComponent<EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(data =>
            {
                var ped = data as PointerEventData;
                if (ped != null && ped.button == PointerEventData.InputButton.Right)
                    onRightClick?.Invoke();
            });
            trigger.triggers.Add(entry);
        }

        private static string TruncateName(string name, int max)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= max ? name : name.Substring(0, max - 1) + "â€¦";
        }

        // â”€â”€ Drag-from-picker â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Mirrors BuildingsRuntimeEditor.Picker.cs (BuildDragGhost / OnPickerSlotPointerDown
        // / UpdatePickerDrag / CancelPickerDrag). Items are center-pivot so we drop
        // directly at worldPos (no Y bottom-center correction needed).

        /// <summary>Build the floating ghost preview that follows the cursor during drag.</summary>
        private void BuildDragGhost()
        {
            if (_dragGhostGo != null) return;

            _dragGhostGo = EditorUIHelpers.CreateUI("ItemDragGhost", _canvas.transform);
            _dragGhostRt = _dragGhostGo.GetComponent<RectTransform>();
            _dragGhostRt.sizeDelta = new Vector2(48f, 48f);
            _dragGhostRt.anchorMin = _dragGhostRt.anchorMax = new Vector2(0.5f, 0.5f);
            _dragGhostRt.pivot     = new Vector2(0.5f, 0.5f);

            // Outline child â€” extends DRAG_GHOST_BORDER px outward.
            var outlineGo = EditorUIHelpers.CreateUI("Outline", _dragGhostGo.transform);
            var outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-DRAG_GHOST_BORDER, -DRAG_GHOST_BORDER);
            outlineRt.offsetMax = new Vector2( DRAG_GHOST_BORDER,  DRAG_GHOST_BORDER);
            _dragGhostOutline               = outlineGo.AddComponent<Image>();
            _dragGhostOutline.color         = DRAG_GHOST_OUTLINE;
            _dragGhostOutline.raycastTarget = false;

            // Sprite child â€” fills the ghost rect, preserves aspect.
            var spriteGo = EditorUIHelpers.CreateUI("Sprite", _dragGhostGo.transform);
            var spriteRt = spriteGo.GetComponent<RectTransform>();
            spriteRt.anchorMin = Vector2.zero;
            spriteRt.anchorMax = Vector2.one;
            spriteRt.offsetMin = spriteRt.offsetMax = Vector2.zero;
            _dragGhostImg                = spriteGo.AddComponent<Image>();
            _dragGhostImg.raycastTarget  = false;
            _dragGhostImg.preserveAspect = true;
            _dragGhostImg.color          = DRAG_GHOST_TINT;

            var cg = _dragGhostGo.AddComponent<CanvasGroup>();
            cg.blocksRaycasts     = false;
            cg.ignoreParentGroups = false;
            _dragGhostGo.transform.SetAsLastSibling();
            _dragGhostGo.SetActive(false);
        }

        /// <summary>Size the ghost rect so its on-screen pixel size matches the icon's world footprint.</summary>
        private void SizeDragGhostToWorldFootprint(ItemDefinition def)
        {
            if (def == null || _dragGhostRt == null) return;

            // Derive world size from the icon's pixel rect via PPU (defaults to 1Ã—1u when missing).
            float worldW = 1f, worldH = 1f;
            var spr = def.icon ?? def.iconLarge ?? def.iconSmall;
            if (spr != null)
            {
                worldW = Mathf.Max(0.25f, spr.rect.width  / ITEM_PPU);
                worldH = Mathf.Max(0.25f, spr.rect.height / ITEM_PPU);
            }

            float pxPerWorldUnit = 32f; // safe default for windowed view
            if (_mainCamera != null && _mainCamera.orthographic && _mainCamera.orthographicSize > 0.001f)
                pxPerWorldUnit = Screen.height / (2f * _mainCamera.orthographicSize);

            float scaleFactor = (_canvas != null && _canvas.scaleFactor > 0.001f) ? _canvas.scaleFactor : 1f;
            float wPx = worldW * pxPerWorldUnit / scaleFactor;
            float hPx = worldH * pxPerWorldUnit / scaleFactor;
            // Min visible size so 16Ã—16 icons aren't invisible at high zooms.
            wPx = Mathf.Max(wPx, 32f);
            hPx = Mathf.Max(hPx, 32f);
            const float MAX_PX = 256f;
            if (wPx > MAX_PX || hPx > MAX_PX)
            {
                float k = MAX_PX / Mathf.Max(wPx, hPx);
                wPx *= k; hPx *= k;
            }
            _dragGhostRt.sizeDelta = new Vector2(wPx, hPx);
        }

        /// <summary>EventTrigger.PointerDown handler: record drag origin.</summary>
        private void OnPickerSlotPointerDown(string itemId)
        {
            _pickerDragItemId      = itemId;
            // MouseInputManager wraps the OR-of-new-and-legacy fallback so the
            // start screen position survives an InputSystem-drops-events frame.
            _pickerDragStartScreen = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
        }

        /// <summary>State machine: arm drag → follow cursor → drop on map.</summary>
        private void UpdatePickerDrag()
        {
            // Don't bail when Mouse.current is null — MouseInputManager has a
            // legacy backend fallback that keeps reading.
            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();

            // Phase 1 â€” waiting for threshold
            if (!_pickerDragging && !string.IsNullOrEmpty(_pickerDragItemId))
            {
                if (Valkur.Core.Input.MouseInputManager.IsLeftMouseButtonPressed())
                {
                    if (Vector2.Distance(screenPos, _pickerDragStartScreen) >= PICKER_DRAG_THRESHOLD)
                    {
                        var def = FindItemById(_pickerDragItemId);
                        if (def != null)
                        {
                            _pickerDragging  = true;
                            _selectedItemId  = _pickerDragItemId;
                            RefreshPicker();
                            BuildDragGhost();
                            var spr = def.icon ?? def.iconLarge ?? def.iconSmall;
                            _dragGhostImg.sprite  = spr;
                            _dragGhostImg.enabled = spr != null;
                            _dragGhostImg.color   = DRAG_GHOST_TINT;
                            SizeDragGhostToWorldFootprint(def);
                            _dragGhostGo.transform.SetAsLastSibling();
                            _dragGhostGo.SetActive(true);
                            SetStatus($"Dragging '{def.displayName ?? def.itemId}' â€” release over the map to drop.");
                        }
                    }
                }
                else
                {
                    // Released before threshold â†’ treat as plain click (handled by Button.onClick).
                    _pickerDragItemId = null;
                }
                return;
            }

            if (!_pickerDragging) return;

            // Phase 2 â€” ghost follows cursor on the editor canvas.
            if (_dragGhostRt != null && _canvas != null)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.GetComponent<RectTransform>(),
                    screenPos,
                    _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _mainCamera,
                    out Vector2 canvasPos);
                _dragGhostRt.anchoredPosition = canvasPos;
            }

            // Pulse outline alpha (5 Hz sine, 0.35 â†’ 1.0).
            if (_dragGhostOutline != null)
            {
                float t = (Mathf.Sin(Time.time * Mathf.PI * 5f) + 1f) * 0.5f;
                var c = DRAG_GHOST_OUTLINE;
                c.a = Mathf.Lerp(0.35f, 1.0f, t);
                _dragGhostOutline.color = c;
            }

            // Drop on LMB release
            if (Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonReleasedThisFrame())
            {
                bool overUi = EventSystem.current != null
                           && EventSystem.current.IsPointerOverGameObject();
                if (!overUi)
                {
                    if (_mainCamera == null) _mainCamera = Camera.main;
                    if (_mainCamera != null)
                    {
                        Vector3 sp = new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z);
                        Vector3 worldPos = _mainCamera.ScreenToWorldPoint(sp);
                        worldPos.z = 0f;
                        // Items are center-pivot â€” no Y correction needed (unlike Buildings).
                        SpawnAt(worldPos);
                    }
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
            _pickerDragging   = false;
            _pickerDragItemId = null;
            if (_dragGhostGo != null) _dragGhostGo.SetActive(false);
        }
    }
}
