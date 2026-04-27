using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Data;
using Valkur.Gameplay.Editors.EditorKit;
using Valkur.Gameplay.Inventory;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor — world interaction (Spawn / Delete modes) and toolbar buttons
    /// (Add / Remove / Add-On-System) and Undo/Redo.
    ///
    /// Mirrors Python <c>roguelike_editors/items/services/drop_service.py</c>:
    ///  • Spawn: <c>spawn_item_at_screen_pos</c> → click on map drops the selected item.
    ///  • Spawn at player (RMB on icon) → <c>spawn_at_player</c>.
    ///  • Delete: <c>delete_drop_at_screen_pos</c> → click on a drop removes it.
    ///  • Add → enter Spawn mode, Remove → enter Delete mode.
    ///  • Add-On-System → would create a new ItemDefinition asset; here we surface a
    ///    runtime-friendly toast (asset creation requires the Editor; documented).
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        // ── World mouse handling per mode ──

        /// <summary>
        /// Hover-test world drops every frame; on LMB pressed-this-frame outside UI:
        ///  • Delete mode → DeleteAtWorld.
        ///  • Any other mode with a hovered drop → SetActiveInstance (mirrors Buildings).
        ///  • Spawn mode without hovered drop → SpawnAt (legacy click-to-spawn path).
        /// </summary>
        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Compute world cursor position (always — even without click — so we can hover).
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) { _hoveredInstance = null; return; }

            Vector2 screenPos = mouse.position.ReadValue();
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z);
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(sp);
            worldPos.z = 0f;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _hoveredInstance = overUi ? null : FindHoveredPickup(worldPos);

            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (overUi) return;

            // While a drag-from-picker is in progress, swallow this click — the drag
            // ghost owns the LMB-release placement and the click should not also fire.
            if (_pickerDragging) return;

            switch (_mode)
            {
                case EditorMode.Delete:
                    if (_hoveredInstance != null) DeletePickup(_hoveredInstance);
                    else DeleteAtWorld(worldPos);
                    break;
                case EditorMode.Spawn:
                    if (_hoveredInstance != null) SetActiveInstance(_hoveredInstance);
                    else SpawnAt(worldPos);
                    break;
                case EditorMode.Select:
                default:
                    if (_hoveredInstance != null) SetActiveInstance(_hoveredInstance);
                    else { _selectedInstance = null; RefreshProperties(); RebuildInstancesList(); }
                    break;
            }
        }

        /// <summary>AABB-test cursor against every WorldPickup's SpriteRenderer.bounds.</summary>
        private WorldPickup FindHoveredPickup(Vector3 worldPos)
        {
            WorldPickup best    = null;
            float bestDistSq    = float.PositiveInfinity;
            for (int i = 0; i < _instances.Count; i++)
            {
                var p = _instances[i];
                if (p == null) continue;
                var sr = p.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null) continue;
                if (!sr.bounds.Contains(new Vector3(worldPos.x, worldPos.y, sr.bounds.center.z))) continue;
                // When stacked, prefer the front-most (highest Y as drawn last).
                float d = (p.transform.position - worldPos).sqrMagnitude;
                if (d < bestDistSq) { bestDistSq = d; best = p; }
            }
            return best;
        }

        // ── Outline FX (sprite tinting) ─────────────────────────────────────────

        /// <summary>Paint hovered/active sprites with cyan/yellow tints; restore others.</summary>
        private void UpdateOutlineState()
        {
            // Restore any sprite that is no longer hovered or active.
            // Iterate over a copy of the keys to allow removal.
            var keys = new List<SpriteRenderer>(_originalSpriteColors.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var sr = keys[i];
                if (sr == null) { _originalSpriteColors.Remove(sr); continue; }
                bool isHovered = _hoveredInstance  != null && sr == _hoveredInstance.GetComponent<SpriteRenderer>();
                bool isActive  = _selectedInstance != null && sr == _selectedInstance.GetComponent<SpriteRenderer>();
                if (!isHovered && !isActive)
                {
                    sr.color = _originalSpriteColors[sr];
                    _originalSpriteColors.Remove(sr);
                }
            }

            ApplyTint(_hoveredInstance,  _mode == EditorMode.Delete ? DELETE_RED : HOVER_CYAN);
            ApplyTint(_selectedInstance, ACTIVE_YELLOW);
        }

        private void ApplyTint(WorldPickup pickup, Color tint)
        {
            if (pickup == null) return;
            var sr = pickup.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            if (!_originalSpriteColors.ContainsKey(sr))
                _originalSpriteColors[sr] = sr.color;
            sr.color = tint;
        }

        private void ClearAllSpriteTints()
        {
            foreach (var kv in _originalSpriteColors)
                if (kv.Key != null) kv.Key.color = kv.Value;
            _originalSpriteColors.Clear();
        }

        // ── Spawn ──

        /// <summary>Spawn the currently-selected item at <paramref name="worldPos"/>.</summary>
        private void SpawnAt(Vector3 worldPos)
        {
            var def = FindItemById(_selectedItemId);
            if (def == null)
            {
                SetStatus("Pick an item from the grid before spawning.");
                return;
            }
            var pickup = DropSystem.SpawnDrop(def, 1, worldPos);
            if (pickup == null) return;

            var captured = pickup;
            _undo.Record(new UndoStack.LambdaCommand(
                $"Spawn {def.itemId}",
                doAction: () =>
                {
                    // Re-execute (after Undo) → respawn at same position.
                    if (captured == null)
                        captured = DropSystem.SpawnDrop(def, 1, worldPos);
                },
                undoAction: () =>
                {
                    if (captured != null) Destroy(captured.gameObject);
                    captured = null;
                }));

            ForceRefreshInstances();
            SetStatus($"Spawned '{def.displayName ?? def.itemId}' at ({worldPos.x:F1},{worldPos.y:F1}).");
        }

        /// <summary>Spawn one of <paramref name="itemId"/> at the current player position.</summary>
        private void SpawnAtPlayer(string itemId)
        {
            var def = FindItemById(itemId);
            if (def == null) { SetStatus($"Item '{itemId}' not in catalog."); return; }
            var player = GameObject.FindWithTag("Player");
            if (player == null) { SetStatus("No Player found in scene."); return; }
            SpawnAt(player.transform.position);
        }

        // ── Delete ──

        private void DeleteAtWorld(Vector3 worldPos)
        {
            var pickup = FindNearestPickup(worldPos, maxRadius: 1.0f);
            if (pickup == null) { SetStatus("No drop near cursor."); return; }
            DeletePickup(pickup);
        }

        /// <summary>Delete a specific pickup with undo support; clears selection if needed.</summary>
        public void DeletePickup(WorldPickup pickup)
        {
            if (pickup == null) return;
            var def = pickup.Item;
            int qty = pickup.Quantity;
            var pos = pickup.transform.position;
            string itemId = def != null ? def.itemId : "?";

            _undo.Record(new UndoStack.LambdaCommand(
                $"Delete {itemId}",
                doAction:   () => { /* original deletion already executed below */ },
                undoAction: () => { if (def != null) DropSystem.SpawnDrop(def, qty, pos); }));

            if (_selectedInstance == pickup) _selectedInstance = null;
            if (_hoveredInstance  == pickup) _hoveredInstance  = null;
            Destroy(pickup.gameObject);
            ForceRefreshInstances();
            RefreshProperties();
            SetStatus($"Deleted '{itemId}'.");
        }

        private WorldPickup FindNearestPickup(Vector3 worldPos, float maxRadius)
        {
            WorldPickup best = null;
            float bestSq = maxRadius * maxRadius;
            for (int i = 0; i < _instances.Count; i++)
            {
                var p = _instances[i];
                if (p == null) continue;
                float sq = (p.transform.position - worldPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = p; }
            }
            return best;
        }

        // ── Toolbar buttons (Add / Remove / Add-On-System) ──

        private void OnAddClicked()
        {
            SetMode(EditorMode.Spawn);
            Toast(string.IsNullOrEmpty(_selectedItemId)
                ? "Add: pick an item from the grid first, then click on the map."
                : $"Add '{_selectedItemId}': click on the map to drop one.");
        }

        private void OnRemoveClicked()
        {
            SetMode(EditorMode.Delete);
            Toast("Remove: click on a world drop to delete it.");
        }

        private void OnAddOnSystemClicked()
        {
            // Python's "add_item_on_system" persists a new entry into items.json. The
            // Unity equivalent is creating a new ItemDefinition asset, which is an
            // Editor-only action. At runtime we surface a clear instruction.
            Toast("Add-on-system: create new ItemDefinition via Project ▸ Create ▸ Valkur ▸ Data ▸ Item Definition (Editor only).");
        }

        // ── Undo / Redo ──

        private void DoUndo()
        {
            if (!_undo.CanUndo) { Toast("Nothing to undo."); return; }
            string label = _undo.PeekUndoLabel();
            _undo.Undo();
            ForceRefreshInstances();
            Toast($"Undo: {label}");
        }

        private void DoRedo()
        {
            if (!_undo.CanRedo) { Toast("Nothing to redo."); return; }
            string label = _undo.PeekRedoLabel();
            _undo.Redo();
            ForceRefreshInstances();
            Toast($"Redo: {label}");
        }

        // ── Keyboard shortcuts ──

        private void HandleKeyboardShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool ctrl = kb.ctrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame) DoUndo();
            if (ctrl && kb.yKey.wasPressedThisFrame) DoRedo();
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (_tutorial != null && _tutorial.activeSelf) _tutorial.SetActive(false);
                else Deactivate();
            }
        }
    }
}
