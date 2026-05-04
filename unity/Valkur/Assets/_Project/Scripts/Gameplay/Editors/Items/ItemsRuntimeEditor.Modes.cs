using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Valkur.Core;
using Valkur.Data;
using Valkur.UIKit;
using Valkur.Gameplay.Inventory;
using Valkur.Gameplay.WorldDrops;

namespace Valkur.Gameplay.Items
{
    /// <summary>
    /// Items Editor â€” world interaction (Spawn / Delete modes) and toolbar buttons
    /// (Add / Remove / Add-On-System) and Undo/Redo.
    ///
    /// Mirrors Python <c>roguelike_editors/items/services/drop_service.py</c>:
    ///  â€¢ Spawn: <c>spawn_item_at_screen_pos</c> â†’ click on map drops the selected item.
    ///  â€¢ Spawn at player (RMB on icon) â†’ <c>spawn_at_player</c>.
    ///  â€¢ Delete: <c>delete_drop_at_screen_pos</c> â†’ click on a drop removes it.
    ///  â€¢ Add â†’ enter Spawn mode, Remove â†’ enter Delete mode.
    ///  â€¢ Add-On-System â†’ would create a new ItemDefinition asset; here we surface a
    ///    runtime-friendly toast (asset creation requires the Editor; documented).
    /// </summary>
    public partial class ItemsRuntimeEditor
    {
        // â”€â”€ World mouse handling per mode â”€â”€

        /// <summary>
        /// Hover-test world drops every frame; on LMB pressed-this-frame outside UI:
        ///  • Delete mode → DeleteAtWorld.
        ///  • Any other mode with a hovered drop → SetActiveInstance (mirrors Buildings).
        ///  • Spawn mode without hovered drop → SpawnAt (legacy click-to-spawn path).
        ///
        /// RMB on a hovered drop starts a drag-to-move: the pickup follows the
        /// cursor while the button is held, and the new world position is
        /// committed to the persistence service on release.
        /// </summary>
        private void HandleMapInteraction()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // Compute world cursor position (always — even without click — so we can hover).
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) { _hoveredInstance = null; return; }

            Vector2 screenPos = Valkur.Core.Input.MouseInputManager.GetScreenMousePosition();
            Vector3 sp = new Vector3(screenPos.x, screenPos.y, -_mainCamera.transform.position.z);
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(sp);
            worldPos.z = 0f;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _hoveredInstance = overUi ? null : FindHoveredPickup(worldPos);

            // ── RMB drag-to-move: takes priority over LMB so RMB-on-hovered-drop
            //    never accidentally falls through to the LMB switch below.
            UpdateRmbDragMove(worldPos, overUi);
            if (_movingInstance != null) return;

            if (!Valkur.Core.Input.MouseInputManager.WasLeftMouseButtonPressedThisFrame()) return;
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

        // ── RMB drag-to-move ──────────────────────────────────────────────────

        /// <summary>
        /// Mirrors how the Buildings editor moves placed objects: while RMB is
        /// held over a world drop, the pickup follows the cursor; on release the
        /// new position is committed to <see cref="ItemDropService"/> so a save
        /// cycle restores it. Uses an Undo command keyed on the dropId so
        /// Ctrl+Z reverts the entire move atomically.
        /// </summary>
        private void UpdateRmbDragMove(Vector3 worldPos, bool overUi)
        {
            // Centralized input — direct Mouse.current reads are forbidden by the
            // input-centralization guard test (see CLAUDE.md "Input pipeline").
            bool rmbDown    = Valkur.Core.Input.MouseInputManager.WasRightMouseButtonPressedThisFrame();
            bool rmbHeld    = Valkur.Core.Input.MouseInputManager.IsRightMouseButtonPressed();
            bool rmbRelease = Valkur.Core.Input.MouseInputManager.WasRightMouseButtonReleasedThisFrame();

            // Begin: RMB pressed over a hovered drop, outside UI.
            if (rmbDown && !overUi && _movingInstance == null && _hoveredInstance != null)
            {
                _movingInstance        = _hoveredInstance;
                _moveDropId            = _movingInstance.DropId;
                _moveStartWorldPos     = _movingInstance.transform.position;
                SetActiveInstance(_movingInstance);
                SetStatus($"Moving '{_movingInstance.Item?.itemId}'… release RMB to drop, Esc to cancel.");
                return;
            }

            // Track: while RMB held, pickup follows the cursor. SetWorldPosition
            // also re-anchors the bob baseline so the WorldPickup.Update() bob
            // doesn't snap the Y back to the original spawn position mid-drag.
            if (rmbHeld && _movingInstance != null)
            {
                _movingInstance.SetWorldPosition(new Vector3(worldPos.x, worldPos.y,
                    _movingInstance.transform.position.z));
                return;
            }

            // Commit: on release, persist via the service if available.
            if (rmbRelease && _movingInstance != null)
            {
                var landed = _movingInstance.transform.position;
                var startPos = _moveStartWorldPos;
                var moved    = _movingInstance;
                string dropId = _moveDropId;
                var service  = ResolveDropService();

                _movingInstance = null;
                _moveDropId     = null;

                // Lock the moved baseline immediately so the bob baseline reflects
                // the landed Y without waiting for the next held-frame.
                moved.SetWorldPosition(landed);

                if (service != null && !string.IsNullOrEmpty(dropId))
                {
                    service.UpdatePosition(dropId, new Vector2(landed.x, landed.y));
                    _undo.Record(new UndoStack.LambdaCommand(
                        $"Move {moved.Item?.itemId}",
                        doAction: () =>
                        {
                            if (moved != null) moved.SetWorldPosition(landed);
                            service.UpdatePosition(dropId, new Vector2(landed.x, landed.y));
                        },
                        undoAction: () =>
                        {
                            if (moved != null) moved.SetWorldPosition(startPos);
                            service.UpdatePosition(dropId, new Vector2(startPos.x, startPos.y));
                        }));
                }
                RefreshProperties();
                RebuildInstancesList();
                SetStatus($"Moved to ({landed.x:F1}, {landed.y:F1}).");
            }
        }

        /// <summary>Cancel an in-flight RMB move and snap the pickup back to its
        /// original position. Called from the Escape handler.</summary>
        private void CancelRmbMove()
        {
            if (_movingInstance == null) return;
            _movingInstance.SetWorldPosition(_moveStartWorldPos);
            _movingInstance = null;
            _moveDropId     = null;
            SetStatus("Move cancelled.");
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

        // ── Outline FX (line-loop around the sprite bounds) ──────────────────
        // Two child <see cref="ItemOutlineRenderer"/> live for the editor's
        // lifetime (built lazily in EnsureOutlineFx). One follows the hovered
        // pickup with the cyan / red color depending on Delete mode; the other
        // follows the active selection in yellow. Both render on the VFX
        // sorting layer so they sit above the drop sprite without altering its
        // colour — Phase 1 used a tint, which discoloured the icon and didn't
        // look like a "border" at all.

        private void EnsureOutlineFx()
        {
            if (_hoverFx == null)
            {
                var go = new GameObject("ItemHoverOutline");
                go.transform.SetParent(transform, false);
                _hoverFx = go.AddComponent<Valkur.Gameplay.Editors.Items.ItemOutlineRenderer>();
                _hoverFx.Configure(HOVER_CYAN, thicknessWorld: 0.06f);
                _hoverFx.SetVisible(false);
            }
            if (_activeFx == null)
            {
                var go = new GameObject("ItemActiveOutline");
                go.transform.SetParent(transform, false);
                _activeFx = go.AddComponent<Valkur.Gameplay.Editors.Items.ItemOutlineRenderer>();
                _activeFx.Configure(ACTIVE_YELLOW, thicknessWorld: 0.10f, padding: 0.06f);
                _activeFx.SetVisible(false);
            }
        }

        /// <summary>Drive the two outline renderers from the hover/select state.
        /// In Delete mode the hover outline switches to red so the user reads
        /// the destructive intent.</summary>
        private void UpdateOutlineState()
        {
            EnsureOutlineFx();

            // Hover outline (cyan / red).
            if (_hoveredInstance != null)
            {
                var color = _mode == EditorMode.Delete ? DELETE_RED : HOVER_CYAN;
                _hoverFx.Configure(color,
                    thicknessWorld: _mode == EditorMode.Delete ? 0.10f : 0.06f);
                _hoverFx.Follow(_hoveredInstance);
                _hoverFx.SetVisible(true);
            }
            else
            {
                _hoverFx.Follow(null);
                _hoverFx.SetVisible(false);
            }

            // Active selection outline (yellow). Hidden when the same pickup is
            // also hovered to avoid double-stacked outlines flickering.
            if (_selectedInstance != null && _selectedInstance != _hoveredInstance)
            {
                _activeFx.Follow(_selectedInstance);
                _activeFx.SetVisible(true);
            }
            else
            {
                _activeFx.Follow(null);
                _activeFx.SetVisible(false);
            }
        }

        /// <summary>Hide both outlines on Deactivate so the FX don't linger in the world.</summary>
        private void ClearAllSpriteTints()
        {
            if (_hoverFx  != null) { _hoverFx.Follow(null);  _hoverFx.SetVisible(false); }
            if (_activeFx != null) { _activeFx.Follow(null); _activeFx.SetVisible(false); }
            // Legacy tint cache — drained for callers that still reference it
            // until the field is removed in a follow-up cleanup pass.
            _originalSpriteColors.Clear();
        }

        // â”€â”€ Spawn â”€â”€

        /// <summary>Resolve the active <see cref="ItemDropService"/> from the
        /// service locator. Returns null when persistence isn't wired (e.g. the
        /// scene was loaded without GameplaySceneSetup); callers fall back to
        /// the legacy ephemeral path so the editor stays functional in unit
        /// tests / sandbox scenes.</summary>
        private ItemDropService ResolveDropService()
        {
            return ServiceLocator.TryGet<ItemDropService>(out var svc) ? svc : null;
        }

        /// <summary>Default TTL for fresh editor placements: read from the
        /// item's <c>despawnTime</c> field (Python parity). 0 ⇒ infinite,
        /// i.e. authoring drops persist forever until a designer removes them.</summary>
        private static float DefaultEditorTtlFor(ItemDefinition def)
            => def != null ? Mathf.Max(0f, def.despawnTime) : 0f;

        /// <summary>Spawn the currently-selected item at <paramref name="worldPos"/>.</summary>
        private void SpawnAt(Vector3 worldPos)
        {
            var def = FindItemById(_selectedItemId);
            if (def == null)
            {
                SetStatus("Pick an item from the grid before spawning.");
                return;
            }

            float ttl = DefaultEditorTtlFor(def);
            var service = ResolveDropService();

            // Capture instance metadata so Undo / Redo can replay the same
            // drop with the same dropId — keeps the persistent file stable.
            ItemDropInstance pendingInstance = null;
            WorldPickup pendingPickup = null;

            if (service != null)
            {
                pendingInstance = service.SpawnPersistent(def, 1, worldPos, ttl, zoneId: "", source: ItemDropSource.Editor);
                if (pendingInstance != null) pendingPickup = service.GetLivePickup(pendingInstance.dropId);
            }
            else
            {
                pendingPickup = DropSystem.SpawnDrop(def, 1, worldPos);
            }

            if (pendingPickup == null && pendingInstance == null) return;

            string ttlLabel = ttl > 0f ? $"TTL {ttl:F0}s" : "infinite";

            _undo.Record(new UndoStack.LambdaCommand(
                $"Spawn {def.itemId}",
                doAction: () =>
                {
                    // Redo: re-insert the same record (preserves dropId) when
                    // we have a service, fall back to a fresh ephemeral spawn.
                    if (service != null && pendingInstance != null)
                    {
                        if (service.GetLivePickup(pendingInstance.dropId) == null)
                            service.RestorePersistent(pendingInstance.Clone());
                    }
                    else if (pendingPickup == null)
                    {
                        pendingPickup = DropSystem.SpawnDrop(def, 1, worldPos);
                    }
                },
                undoAction: () =>
                {
                    if (service != null && pendingInstance != null)
                        service.RemoveByDropId(pendingInstance.dropId);
                    else if (pendingPickup != null)
                        Destroy(pendingPickup.gameObject);
                    pendingPickup = null;
                }));

            ForceRefreshInstances();
            SetStatus($"Spawned '{def.displayName ?? def.itemId}' at ({worldPos.x:F1},{worldPos.y:F1}) [{ttlLabel}].");
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

        // â”€â”€ Delete â”€â”€

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

            var service = ResolveDropService();
            // Snapshot for Undo: a persistent drop must round-trip its full
            // record so re-inserting preserves dropId + ttl + createdAt.
            ItemDropInstance snapshot = null;
            if (service != null && pickup.IsPersistent && !string.IsNullOrEmpty(pickup.DropId))
            {
                snapshot = service.Get(pickup.DropId)?.Clone();
            }

            _undo.Record(new UndoStack.LambdaCommand(
                $"Delete {itemId}",
                doAction:   () => { /* original deletion already executed below */ },
                undoAction: () =>
                {
                    if (snapshot != null && service != null)
                    {
                        service.RestorePersistent(snapshot.Clone());
                    }
                    else if (def != null)
                    {
                        DropSystem.SpawnDrop(def, qty, pos);
                    }
                }));

            if (_selectedInstance == pickup) _selectedInstance = null;
            if (_hoveredInstance  == pickup) _hoveredInstance  = null;

            if (service != null && pickup.IsPersistent && !string.IsNullOrEmpty(pickup.DropId))
            {
                // RemoveByDropId destroys the live pickup as part of the call.
                service.RemoveByDropId(pickup.DropId);
            }
            else
            {
                Destroy(pickup.gameObject);
            }

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

        // â”€â”€ Toolbar buttons (Add / Remove / Add-On-System) â”€â”€

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
            Toast("Add-on-system: create new ItemDefinition via Project â–¸ Create â–¸ Valkur â–¸ Data â–¸ Item Definition (Editor only).");
        }

        // â”€â”€ Undo / Redo â”€â”€

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

        // â”€â”€ Keyboard shortcuts â”€â”€

        private void HandleKeyboardShortcuts()
        {
            // Routed through KeyboardInputManager so the legacy backend keeps
            // these shortcuts working under the InputSystem-drops-events bug.
            bool ctrl = Valkur.Core.Input.KeyboardInputManager.IsCtrlHeld();
            if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Z, KeyCode.Z)) DoUndo();
            if (ctrl && Valkur.Core.Input.KeyboardInputManager.WasKeyPressedThisFrame(Key.Y, KeyCode.Y)) DoRedo();
            if (Valkur.Core.Input.KeyboardInputManager.WasEscapePressedThisFrame())
            {
                if (_movingInstance != null)            CancelRmbMove();
                else if (_tutorial != null && _tutorial.activeSelf) _tutorial.SetActive(false);
                else                                    Deactivate();
            }
        }
    }
}
