from __future__ import annotations

from typing import Optional
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services import (
    pick_spawner_under_cursor,
    screen_to_tile,
    find_instance_in_json,
    persist_drop,
    zone_for_global_tile,
    load_instances_json,
    write_instances_json,
)
from roguelike_editors.spawner.services.persistence import load_spawners_json, generate_instance_id


class SpawnerEditorEventHandler:
    """Event handler for Spawner Editor.

    Responsibilities:
    - Maintain visibility flag.
    - Handle RMB drag to reposition a spawner's anchor tile.
    - Handle MMB panning of the camera; no camera panning on RMB.
    - Suppress gameplay input while dragging/panning.
    """

    def __init__(self, controller: 'SpawnerEditorController'):
        self.controller = controller
        self.model = controller.model
        self.font = controller.font
        self.game = controller.game
        # Camera panning state (MMB only)
        self.panning: bool = False
        self.pan_start: tuple[int, int] = (0, 0)
        self.pan_offset_start: tuple[float, float] = (0.0, 0.0)
        # Drag persistence snapshot
        # {'template_id': str, 'zone': str, 'local_tile': (int,int), 'overrides': dict | None, 'index': int | None}
        self._drag_start_entry: Optional[dict] = None

    # Public API ---------------------------------------------------------------
    def set_game(self, game) -> None:
        self.game = game

    def toggle_visible(self) -> None:
        self.model.visible = not self.model.visible
        # Stop drag when toggling off and update global flags
        try:
            world = getattr(self.game.ecs, 'ecs_world', None)
        except Exception:
            world = None
        if not self.model.visible:
            self.model.dragging = False
            self.model.dragging_eid = None
            self.model.hovered_eid = None
            self.panning = False
            self._drag_start_entry = None
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', None)
                    # Ensure input is re-enabled on hide
                    setattr(world.state, 'spawner_input_suppressed', False)
                    # Mark editor as inactive globally
                    setattr(world.state, 'spawner_editor_active', False)
            except Exception:
                pass
        else:
            # Mark editor as active globally
            try:
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_active', True)
            except Exception:
                pass

    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.model.visible or not self.game:
            return False
        world = getattr(self.game.ecs, 'ecs_world', None)
        camera = getattr(self.game, 'camera', None)
        if not world or not camera:
            return False

        # While the Visuals Picker overlay is open, delegate events to it and block world input
        try:
            ip = getattr(self.controller, 'instance_properties', None)
            if ip is not None and getattr(getattr(ip, 'model', None), 'visuals_picker_open', False):
                handled = False
                try:
                    handled = bool(ip.handle_visuals_picker_event(event, camera))
                except Exception:
                    handled = False
                # Always consume to avoid gameplay interactions under the overlay
                return True if handled or event.type in (
                    pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP, pygame.MOUSEMOTION,
                    pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP
                ) else False
        except Exception:
            pass

        # Add Mode: while active (and before a template is chosen -> placing_template_id is None),
        # block world interactions and allow ESC to cancel, BUT allow MMB panning.
        if getattr(self.model, 'add_mode_active', False) and not getattr(self.model, 'placing_template_id', None):
            # Cancel with ESC
            if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                self.model.add_mode_active = False
                # Hide Templates Manager (switch back to Instances list)
                try:
                    tb = getattr(self.controller, 'spawner_toolbar', None)
                    if tb and getattr(tb, 'model', None) is not None:
                        tb.model.active_tool = 'spawner_list'
                except Exception:
                    pass
                # Mirror to toolbar model to stop blinking
                try:
                    if hasattr(self.controller, 'instance_toolbar') and getattr(self.controller.instance_toolbar, 'model', None) is not None:
                        self.controller.instance_toolbar.model.add_mode_active = False
                        # Also clear dropdown list if present
                        try:
                            self.controller.instance_toolbar.model.add_templates = []
                        except Exception:
                            pass
                except Exception:
                    pass
                # Re-enable gameplay input
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                return True
            # Consume other inputs so the world does not react while template selection is required,
            # but allow MMB panning to work.
            if event.type in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEBUTTONUP):
                btn = getattr(event, 'button', None)
                if btn == 2:
                    # Let MMB events pass (handled below for panning)
                    pass
                else:
                    return True
            elif event.type == pygame.MOUSEMOTION:
                # Allow motion when panning with MMB, otherwise consume
                if not self.panning:
                    return True
            elif event.type in (pygame.MOUSEWHEEL, pygame.KEYDOWN, pygame.KEYUP):
                return True

        # Remove-mode: allow ESC to exit mode when no deletion is pending
        if event.type == pygame.KEYDOWN and getattr(self.model, 'remove_mode_active', False) and not getattr(self.model, 'pending_delete_confirm', None):
            if event.key == pygame.K_ESCAPE:
                self.model.remove_mode_active = False
                # Mirror into toolbar model if available
                try:
                    tb = getattr(self.controller, 'instance_toolbar', None)
                    if tb and getattr(tb, 'model', None) is not None:
                        tb.model.remove_mode_active = False
                except Exception:
                    pass
                # Clear world flags
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_remove_mode', False)
                        setattr(world.state, 'spawner_remove_candidate_eid', None)
                except Exception:
                    pass
                # Restore Instances panel by activating 'spawner_list'
                try:
                    main_tb = getattr(self.controller, 'spawner_toolbar', None)
                    if main_tb and getattr(main_tb, 'model', None) is not None:
                        main_tb.model.active_tool = 'spawner_list'
                except Exception:
                    pass
                return True

        # Placement mode: cancel with ESC
        if event.type == pygame.KEYDOWN and getattr(self.model, 'placing_template_id', None):
            if event.key == pygame.K_ESCAPE:
                self.model.placing_template_id = None
                # Stop Add button blinking upon cancel
                try:
                    self.model.add_mode_active = False
                    if hasattr(self.controller, 'instance_toolbar') and getattr(self.controller.instance_toolbar, 'model', None) is not None:
                        self.controller.instance_toolbar.model.add_mode_active = False
                except Exception:
                    pass
                # Re-enable gameplay input when leaving placement
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                # Restore Instances panel by activating 'spawner_list'
                try:
                    tb = getattr(self.controller, 'spawner_toolbar', None)
                    if tb and getattr(tb, 'model', None) is not None:
                        tb.model.active_tool = 'spawner_list'
                except Exception:
                    pass
                return True

        # Update hover on mouse movement
        if event.type == pygame.MOUSEMOTION:
            mx, my = event.pos
            self.model.hovered_eid = pick_spawner_under_cursor(world, camera, mx, my)
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', self.model.hovered_eid)
                    # In remove mode, also mark candidate for red highlight
                    if getattr(self.model, 'remove_mode_active', False):
                        setattr(world.state, 'spawner_remove_candidate_eid', self.model.hovered_eid)
                    else:
                        setattr(world.state, 'spawner_remove_candidate_eid', None)
            except Exception:
                pass
            # Handle camera panning while moving mouse
            if self.panning and not self.model.dragging:
                dx = mx - self.pan_start[0]
                dy = my - self.pan_start[1]
                camera.offset_x = self.pan_offset_start[0] - dx / (getattr(camera, 'zoom', 1.0) or 1.0)
                camera.offset_y = self.pan_offset_start[1] - dy / (getattr(camera, 'zoom', 1.0) or 1.0)
                return True

        # Placement mode: LMB places a new instance on map
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1 and getattr(self.model, 'placing_template_id', None):
            tpl_id = self.model.placing_template_id
            mx, my = event.pos
            tx, ty = screen_to_tile(camera, mx, my)
            zone = zone_for_global_tile(int(tx), int(ty)) or 'lobby'
            off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
            local_tx, local_ty = int(tx - off_x), int(ty - off_y)
            # Persist instance with deduplication by (template_id, zone, tile)
            try:
                instances = load_instances_json()
            except Exception:
                instances = []
            new_entry = {
                'template_id': tpl_id,
                'zone': zone,
                'tile': [local_tx, local_ty],
            }
            # If an entry already exists at this key, replace it and preserve id
            try:
                data2, idx, _ = find_instance_in_json(tpl_id, zone, (local_tx, local_ty))
                if data2 is not None:
                    instances = data2
            except Exception:
                idx = None
            try:
                if idx is not None:
                    prev_id = instances[idx].get('id')
                    if prev_id:
                        new_entry['id'] = prev_id
                    instances[idx] = new_entry
                else:
                    # Assign a unique id immediately to avoid transient duplicates without ids
                    existing_ids = {str(e.get('id')) for e in instances if e.get('id')}
                    new_entry['id'] = generate_instance_id(new_entry, existing_ids)
                    instances.append(new_entry)
            except Exception:
                # Fallback: append
                try:
                    instances.append(new_entry)
                except Exception:
                    pass
            try:
                write_instances_json(instances)
            except Exception:
                pass
            # Spawn entity immediately
            try:
                # load template dict
                tpls = {t.get('id'): t for t in (load_spawners_json() or []) if isinstance(t, dict)}
                tpl = tpls.get(tpl_id)
                if tpl:
                    trigger = dict(tpl.get('trigger', {}))
                    policy = dict(tpl.get('policy', {}))
                    waves = list(tpl.get('waves', []))
                    spawner_type = tpl.get('spawner_type', 'invisible')
                    # frames
                    from roguelike_engine.config import config as _cfg
                    fps = getattr(_cfg, 'FPS', 60)
                    cooldown_s = float(policy.get('cooldown_s', 10.0))
                    cooldown_frames = int(round(cooldown_s * fps))
                    from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
                    from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
                    cfg = SpawnerConfig(
                        template_id=tpl_id,
                        zone=zone,
                        anchor_tile=(int(tx), int(ty)),
                        spawner_type=spawner_type,
                        trigger=trigger,
                        policy=policy,
                        waves=waves,
                        cooldown_frames=cooldown_frames,
                    )
                    eid = world.create_entity()
                    world.components['SpawnerConfig'][eid] = cfg
                    world.components['SpawnerState'][eid] = SpawnerState()
            except Exception:
                pass
            # Refresh UI list of instances if visible
            try:
                self.controller.spawner_instances.refresh_from_disk()
            except Exception:
                pass
            # Exit placement mode, stop Add button blinking, re-enable input, and restore Instances panel
            self.model.placing_template_id = None
            # Tutorial pulse: placement done
            try:
                setattr(self.model, 'tutorial_placement_done_pulse', True)
            except Exception:
                pass
            try:
                self.model.add_mode_active = False
                if hasattr(self.controller, 'instance_toolbar') and getattr(self.controller.instance_toolbar, 'model', None) is not None:
                    self.controller.instance_toolbar.model.add_mode_active = False
            except Exception:
                pass
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            # Activate 'spawner_list' so Instances panel reappears
            try:
                tb = getattr(self.controller, 'spawner_toolbar', None)
                if tb and getattr(tb, 'model', None) is not None:
                    tb.model.active_tool = 'spawner_list'
            except Exception:
                pass
            return True

        # MMB down: start camera panning (always available while editor visible)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 2:
            mx, my = event.pos
            self.panning = True
            self.pan_start = (mx, my)
            self.pan_offset_start = (camera.offset_x, camera.offset_y)
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', True)
            except Exception:
                pass
            return True

        # MMB up: stop camera panning
        if event.type == pygame.MOUSEBUTTONUP and event.button == 2:
            if self.panning:
                self.panning = False
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                return True

        # Remove mode: LMB click on a spawner asks for deletion confirmation
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1 and getattr(self.model, 'remove_mode_active', False):
            mx, my = event.pos
            eid = self.model.hovered_eid or pick_spawner_under_cursor(world, camera, mx, my)
            if eid is None:
                return False
            # Build pending delete confirmation payload
            try:
                cfg = world.components['SpawnerConfig'][eid]
                zone = cfg.zone
                off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                tx, ty = cfg.anchor_tile
                local_tx, local_ty = int(tx - off_x), int(ty - off_y)
                tpl_id = cfg.template_id
                self.model.pending_delete_confirm = {
                    'eid': eid,
                    'template_id': tpl_id,
                    'zone': zone,
                    'local_tile': (local_tx, local_ty),
                }
                # Tutorial pulse: open delete confirmation
                try:
                    setattr(self.model, 'tutorial_delete_confirm_open_pulse', True)
                except Exception:
                    pass
                # Suppress gameplay input while prompt is open
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', True)
                except Exception:
                    pass
                return True
            except Exception:
                return False

        # RMB down: start drag if clicking near a spawner anchor; otherwise do nothing
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            # Disable drag/pan when in remove mode
            if getattr(self.model, 'remove_mode_active', False):
                return True
            mx, my = event.pos
            # Only consume RMB if hovering a spawner
            eid = self.model.hovered_eid or pick_spawner_under_cursor(world, camera, mx, my)
            if eid is not None:
                self.model.dragging = True
                self.model.dragging_eid = eid
                # Tutorial pulse: drag started
                try:
                    setattr(self.model, 'tutorial_drag_started_pulse', True)
                except Exception:
                    pass
                # Suppress gameplay input (dash/spells) while dragging
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', True)
                except Exception:
                    pass
                # Snapshot original instance entry for persistence (capture id too)
                try:
                    cfg = world.components['SpawnerConfig'][eid]
                    zone = cfg.zone
                    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                    cur_tx, cur_ty = cfg.anchor_tile
                    local_start = (int(cur_tx - off_x), int(cur_ty - off_y))
                    tpl_id = cfg.template_id
                    data, idx, overrides = find_instance_in_json(tpl_id, zone, local_start)
                    inst_id = None
                    try:
                        if idx is not None:
                            inst_id = data[idx].get('id')
                    except Exception:
                        inst_id = None
                    self._drag_start_entry = {
                        'template_id': tpl_id,
                        'zone': zone,
                        'local_tile': local_start,
                        'overrides': overrides,
                        'index': idx,
                        'id': inst_id,
                    }
                except Exception:
                    self._drag_start_entry = None
                return True
            # Not hovering a spawner: do nothing (consume to avoid gameplay actions)
            return True

        # RMB up: stop drag
        if event.type == pygame.MOUSEBUTTONUP and event.button == 3:
            if self.model.dragging:
                self.model.dragging = False
                eid = self.model.dragging_eid
                self.model.dragging_eid = None
                # ensure hover state remains accurate
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_editor_hovered_eid', self.model.hovered_eid)
                except Exception:
                    pass
                # Decide zone and persist (or ask for confirmation if zone changed)
                try:
                    if eid is not None:
                        cfg = world.components['SpawnerConfig'][eid]
                        tx, ty = cfg.anchor_tile
                        proposed_zone = zone_for_global_tile(int(tx), int(ty)) or cfg.zone
                        orig_zone = None
                        orig_local = None
                        if self._drag_start_entry:
                            orig_zone = self._drag_start_entry.get('zone')
                            if self._drag_start_entry.get('local_tile') is not None:
                                try:
                                    lx, ly = self._drag_start_entry['local_tile']
                                    orig_local = (int(lx), int(ly))
                                except Exception:
                                    orig_local = None
                        # If changed zone, require user confirmation
                        if orig_zone and proposed_zone and proposed_zone != orig_zone:
                            self.model.pending_zone_confirm = {
                                'eid': eid,
                                'orig_zone': orig_zone,
                                'proposed_zone': proposed_zone,
                                'orig_local': orig_local,
                            }
                            # Tutorial pulse: zone confirm dialog opened
                            try:
                                setattr(self.model, 'tutorial_zone_confirm_open_pulse', True)
                            except Exception:
                                pass
                            # Keep input suppressed until decision
                            return True
                        # Otherwise persist directly
                        persist_drop(world, eid, self._drag_start_entry)
                        # Tutorial pulse: persisted new position
                        try:
                            setattr(self.model, 'tutorial_persist_drop_pulse', True)
                        except Exception:
                            pass
                        # Refresh instances list UI if visible
                        try:
                            self.controller.spawner_instances.refresh_from_disk()
                        except Exception:
                            pass
                except Exception:
                    pass
                # Re-enable gameplay input after drag ends if no pending confirm
                if not self.model.pending_zone_confirm:
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', False)
                    except Exception:
                        pass
                return True
            # Not dragging: ignore RMB up (do not affect MMB panning)
            return True

        # Mouse move while dragging: update anchor tile of selected spawner
        if event.type == pygame.MOUSEMOTION and self.model.dragging and self.model.dragging_eid is not None:
            mx, my = event.pos
            tx, ty = screen_to_tile(camera, mx, my)
            eid = self.model.dragging_eid
            try:
                cfg = world.components['SpawnerConfig'][eid]
                # Update anchor (global tile coords)
                cfg.anchor_tile = (tx, ty)
                # Optional: invalidate spatial index if spawner affects collision in future
                # world.invalidate_spatial_index()
            except Exception:
                pass
            return True

        # While a zone confirmation is pending, capture Y/N or Enter/Esc
        if event.type == pygame.KEYDOWN and self.model.pending_zone_confirm:
            pending = self.model.pending_zone_confirm
            key = event.key
            # Confirm: Y or Return/Enter
            if key in (pygame.K_y, pygame.K_RETURN, pygame.K_KP_ENTER):
                try:
                    eid = pending.get('eid')
                    orig_zone = pending.get('orig_zone')
                    proposed_zone = pending.get('proposed_zone')
                    if eid is not None:
                        # Persist under new zone, replacing original entry
                        persist_drop(world, eid, self._drag_start_entry, override_zone=proposed_zone, orig_zone=orig_zone)
                        # Update in-memory config zone for future moves
                        try:
                            cfg = world.components['SpawnerConfig'][eid]
                            cfg.zone = proposed_zone
                        except Exception:
                            pass
                        # Refresh instances list UI if visible
                        try:
                            self.controller.spawner_instances.refresh_from_disk()
                        except Exception:
                            pass
                except Exception:
                    pass
                self.model.pending_zone_confirm = None
                # Re-enable gameplay input
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                # Tutorial pulse: confirmed zone change
                try:
                    setattr(self.model, 'tutorial_zone_confirm_yes_pulse', True)
                except Exception:
                    pass
                return True
            # Cancel: N or Esc
            if key in (pygame.K_n, pygame.K_ESCAPE):
                try:
                    eid = pending.get('eid')
                    orig_zone = pending.get('orig_zone')
                    orig_local = pending.get('orig_local')
                    if eid is not None and orig_zone and orig_local:
                        ox, oy = global_map_settings.zone_offsets.get(orig_zone, (0, 0))
                        gx = int(ox + int(orig_local[0]))
                        gy = int(oy + int(orig_local[1]))
                        cfg = world.components['SpawnerConfig'][eid]
                        cfg.anchor_tile = (gx, gy)
                except Exception:
                    pass
                self.model.pending_zone_confirm = None
                # Re-enable gameplay input
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                return True

        # While a delete confirmation is pending, capture Y/N or Enter/Esc
        if event.type == pygame.KEYDOWN and self.model.pending_delete_confirm:
            pending = self.model.pending_delete_confirm
            key = event.key
            # Confirm delete
            if key in (pygame.K_y, pygame.K_RETURN, pygame.K_KP_ENTER):
                try:
                    eid = pending.get('eid')
                    tpl_id = pending.get('template_id')
                    zone = pending.get('zone')
                    local_tile = pending.get('local_tile')
                    data, idx, _ = find_instance_in_json(tpl_id, zone, tuple(local_tile))
                    if idx is not None:
                        try:
                            data.pop(idx)
                        except Exception:
                            pass
                        try:
                            write_instances_json(data)
                        except Exception:
                            pass
                    # Remove entity from world
                    try:
                        if eid is not None:
                            world.remove_entity(eid)
                    except Exception:
                        pass
                    # Refresh UI list and hide properties
                    try:
                        self.controller.spawner_instances.refresh_from_disk()
                    except Exception:
                        pass
                    try:
                        if hasattr(self.controller, 'instance_properties') and getattr(self.controller.instance_properties.model, 'visible', False):
                            self.controller.instance_properties.model.visible = False
                    except Exception:
                        pass
                    # Clear candidate highlight
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_remove_candidate_eid', None)
                            setattr(world.state, 'spawner_editor_hovered_eid', None)
                    except Exception:
                        pass
                except Exception:
                    pass
                self.model.pending_delete_confirm = None
                # Keep remove mode active; re-enable gameplay input after prompt closes
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                # Tutorial pulse: deletion confirmed
                try:
                    setattr(self.model, 'tutorial_delete_done_pulse', True)
                except Exception:
                    pass
                return True
            # Cancel delete
            if key in (pygame.K_n, pygame.K_ESCAPE):
                self.model.pending_delete_confirm = None
                # Re-enable gameplay input after prompt closes
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
                return True

        return False
