from __future__ import annotations

from typing import Optional
import pygame
import logging
from roguelike_ui.ui_blocker import is_blocked

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
from roguelike_editors.spawner.services.persistence import remove_visual_refs_by_building_id
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)


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
        # Live info box dragging (RMB over info box rect)
        self.info_dragging: bool = False
        self.info_drag_offset: tuple[int, int] = (0, 0)

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
            # Also cancel resizing/split dragging if active
            try:
                self.model.resizing_visual = False
                self.model.resizing_visual_bid = None
            except Exception:
                pass
            try:
                self.model.split_drag_active = False
                self.model.split_drag_bid = None
            except Exception:
                pass
            self.panning = False
            self.info_dragging = False
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

        # Stop split drag on any mouse button release (same behavior as Building Editor)
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.model, 'split_drag_active', False):
            # Persist only on LMB release, but always stop dragging
            bid = getattr(self.model, 'split_drag_bid', None)
            self.model.split_drag_active = False
            self.model.split_drag_bid = None
            # Re-enable gameplay input
            try:
                world = getattr(self.game.ecs, 'ecs_world', None)
                if world is not None and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            if bid is not None and event.button == 1:
                try:
                    ip = getattr(self.controller, 'instance_properties', None)
                    ob = ip.visuals._find_building_entity_by_id(int(bid))
                    cur_ratio = float(getattr(ob, 'split_ratio', 0.5)) if ob is not None else None
                except Exception:
                    cur_ratio = None
                if cur_ratio is not None:
                    try:
                        data = svc_load_buildings_instances()
                    except Exception:
                        data = []
                    changed = False
                    for e in data or []:
                        try:
                            if int(e.get('id')) != int(bid):
                                continue
                        except Exception:
                            continue
                        ov = e.get('overrides') or {}
                        if not isinstance(ov, dict):
                            ov = {}
                        ov['split_ratio'] = round(float(cur_ratio), 3)
                        e['overrides'] = ov
                        changed = True
                        break
                    if changed:
                        try:
                            svc_write_buildings_instances(data)
                        except Exception:
                            pass
            return True

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
        # Mouse move while split handle dragging: update split_ratio
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'split_drag_active', False) and getattr(self.model, 'split_drag_bid', None) is not None:
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                cam = getattr(self.game, 'camera', None)
                bid = int(self.model.split_drag_bid)
                ob = ip.visuals._find_building_entity_by_id(int(bid))
            except Exception:
                ob = None
                cam = None
            if ob is not None and cam is not None and getattr(ob, 'image', None) is not None:
                try:
                    mx, my = event.pos
                    bx, by = cam.apply((ob.x, ob.y))
                    _, h_scaled = cam.scale(ob.image.get_size())
                    rel = (float(my) - float(by)) / float(max(h_scaled, 1))
                    rel = max(0.05, min(rel, 0.95))
                    ob.split_ratio = float(rel)
                    try:
                        ob.model._cut_world = int(ob.model.image.get_height() * ob.split_ratio)
                    except Exception:
                        pass
                    try:
                        if getattr(ob, 'controller', None):
                            ob.controller.update_on_camera_change()
                    except Exception:
                        pass
                    # Persist incrementally to buildings_instances.json as overrides.split_ratio
                    try:
                        data = svc_load_buildings_instances()
                    except Exception:
                        data = []
                    changed = False
                    for e in data or []:
                        try:
                            if int(e.get('id')) != int(bid):
                                continue
                        except Exception:
                            continue
                        ov = e.get('overrides') or {}
                        if not isinstance(ov, dict):
                            ov = {}
                        ov['split_ratio'] = round(float(ob.split_ratio), 3)
                        e['overrides'] = ov
                        changed = True
                        break
                    if changed:
                        try:
                            svc_write_buildings_instances(data)
                        except Exception:
                            pass
                except Exception:
                    pass
            return True
        # Mouse move while resizing selected visual building
        if event.type == pygame.MOUSEMOTION and getattr(self.model, 'resizing_visual', False) and getattr(self.model, 'resizing_visual_bid', None) is not None:
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                bid = int(self.model.resizing_visual_bid)
                ob = ip.visuals._find_building_entity_by_id(int(bid))
            except Exception:
                ob = None
            if ob is not None and getattr(ob, 'resize', None) is not None:
                try:
                    start_w, start_h = self.model.resize_start_size or ob.image.get_size()
                except Exception:
                    start_w = start_h = None
                if start_w is not None and start_h is not None:
                    mx, my = event.pos
                    ox, oy = self.model.resize_origin_mouse or (mx, my)
                    dx = int(mx - ox)
                    dy = int(my - oy)
                    new_w = max(8, int(start_w + dx))
                    new_h = max(8, int(start_h + dy))
                    try:
                        ob.resize(int(new_w), int(new_h))
                    except Exception:
                        pass
            return True
            # Visuals hover (cyan): only when a spawner instance is selected and UI is not blocking
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                if ip is not None:
                    # Suppress hover when UI panels under cursor are blocking
                    if is_blocked(mx, my):
                        try:
                            ip.visuals.model.hovered_building_id = None
                        except Exception:
                            pass
                    else:
                        ob = None
                        try:
                            ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                        except Exception:
                            ob = None
                        try:
                            sel = getattr(ip.visuals.model, 'selected_building_id', None)
                        except Exception:
                            sel = None
                        try:
                            if ob is not None and getattr(ob, 'id', None) is not None:
                                bid = int(getattr(ob, 'id'))
                                # Keep hover even if equal to selected (will render as yellow priority)
                                ip.visuals.model.hovered_building_id = bid
                            else:
                                ip.visuals.model.hovered_building_id = None
                        except Exception:
                            pass
            except Exception:
                pass
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

        # Right-click drag for the live info box (when mouse on top of box)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            try:
                rect = getattr(getattr(world, 'state', None), 'spawner_info_rect', None)
            except Exception:
                rect = None
            if rect is not None:
                mx, my = event.pos
                if rect.collidepoint(mx, my):
                    self.info_dragging = True
                    self.info_drag_offset = (mx - rect.left, my - rect.top)
                    try:
                        if hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        pass
                    return True

        if event.type == pygame.MOUSEMOTION and self.info_dragging:
            mx, my = event.pos
            left = int(mx - self.info_drag_offset[0])
            top = int(my - self.info_drag_offset[1])
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_info_pos', (left, top))
            except Exception:
                pass
            return True

        if event.type == pygame.MOUSEBUTTONUP and event.button == 3 and self.info_dragging:
            self.info_dragging = False
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
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
                    prev = instances[idx]
                    prev_id = prev.get('id')
                    if prev_id:
                        new_entry['id'] = prev_id
                    # Preserve previous visuals mapping if present to avoid wiping it
                    try:
                        prev_visuals = prev.get('visuals')
                        if isinstance(prev_visuals, dict) and prev_visuals:
                            new_entry['visuals'] = prev_visuals
                    except Exception:
                        pass
                    instances[idx] = new_entry
                else:
                    # Assign a unique id immediately to avoid transient duplicates without ids
                    existing_ids = {str(e.get('id')) for e in instances if e.get('id')}
                    new_entry['id'] = generate_instance_id(new_entry, existing_ids)
                    instances.append(new_entry)
                try:
                    logging.getLogger(__name__).debug(f"[SpawnerEditorEvents] placement: saving tpl={tpl_id} zone={zone} tile={(local_tx, local_ty)} idx={idx} visuals_len={len((new_entry.get('visuals') or {}))}")
                except Exception:
                    pass
            except Exception:
                # Fallback: append
                try:
                    instances.append(new_entry)
                except Exception:
                    pass
            try:
                write_instances_json(instances)
                try:
                    logging.getLogger(__name__).info(f"[SpawnerEditorEvents] placement: wrote instances ({len(instances)}) last_entry_id={new_entry.get('id')} visuals_len={len((new_entry.get('visuals') or {}))}")
                except Exception:
                    pass
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

        # RMB down on split handle: start split drag (same as Building Editor; prioritize over RMB spawner drag)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            # Skip when in modes that repurpose input
            if getattr(self.model, 'remove_mode_active', False) or getattr(self.model, 'placing_template_id', None):
                return False
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                vmodel = getattr(getattr(ip, 'visuals', None), 'model', None) if ip else None
                sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
            except Exception:
                sel_bid = None
            if sel_bid is not None:
                try:
                    split_rect = getattr(getattr(self.controller, 'view', None), '_last_split_handle_rect', None)
                except Exception:
                    split_rect = None
                if split_rect is not None and pygame.Rect(split_rect).collidepoint(*event.pos):
                    try:
                        self.model.split_drag_active = True
                        self.model.split_drag_bid = int(sel_bid)
                        # Suppress gameplay input while dragging split
                        world = getattr(self.game.ecs, 'ecs_world', None)
                        if world is not None and hasattr(world, 'state'):
                            setattr(world.state, 'spawner_input_suppressed', True)
                    except Exception:
                        self.model.split_drag_active = False
                        self.model.split_drag_bid = None
                    return True

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

        # LMB: select a visual building linked to the currently selected spawner instance,
        # or interact with its handles (delete/resize)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            # Skip when in modes that repurpose LMB
            if getattr(self.model, 'remove_mode_active', False) or getattr(self.model, 'placing_template_id', None):
                return False
            try:
                ip = getattr(self.controller, 'instance_properties', None)
                cam = getattr(self.game, 'camera', None)
                # Only require controller/picker availability; panel may be hidden
                if ip is not None and cam is not None:
                    mx, my = event.pos
                    # First: if we have a selected building, check handle clicks
                    try:
                        vmodel = getattr(ip, 'visuals', None)
                        vmodel = getattr(vmodel, 'model', None)
                        sel_bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
                    except Exception:
                        sel_bid = None
                    if sel_bid is not None:
                        # Read last handle rects from view (overlays computed during render)
                        try:
                            del_rect = getattr(getattr(self.controller, 'view', None), '_last_selected_delete_rect', None)
                            rz_rect  = getattr(getattr(self.controller, 'view', None), '_last_selected_resize_rect', None)
                            rst_rect = getattr(getattr(self.controller, 'view', None), '_last_selected_reset_rect', None)
                            zb_minus = getattr(getattr(self.controller, 'view', None), '_last_z_bottom_minus_rect', None)
                            zb_plus  = getattr(getattr(self.controller, 'view', None), '_last_z_bottom_plus_rect', None)
                            zt_minus = getattr(getattr(self.controller, 'view', None), '_last_z_top_minus_rect', None)
                            zt_plus  = getattr(getattr(self.controller, 'view', None), '_last_z_top_plus_rect', None)
                            split_rect = getattr(getattr(self.controller, 'view', None), '_last_split_handle_rect', None)
                        except Exception:
                            del_rect = rz_rect = rst_rect = None
                            zb_minus = zb_plus = zt_minus = zt_plus = None
                            split_rect = None
                        # Delete handle
                        if del_rect is not None and pygame.Rect(del_rect).collidepoint(mx, my):
                            try:
                                bid = int(sel_bid)
                            except Exception:
                                bid = None
                            if bid is not None:
                                # 1) Clear visuals refs in spawners_instances.json
                                try:
                                    remove_visual_refs_by_building_id(int(bid))
                                except Exception:
                                    pass
                                # 2) Remove from buildings_instances.json
                                try:
                                    data = svc_load_buildings_instances()
                                except Exception:
                                    data = []
                                kept = []
                                for e in data or []:
                                    try:
                                        if int(e.get('id')) == int(bid):
                                            continue
                                    except Exception:
                                        pass
                                    kept.append(e)
                                try:
                                    svc_write_buildings_instances(kept)
                                except Exception:
                                    pass
                                # 3) Remove entity from world/editor lists
                                try:
                                    ip._remove_building_entity_by_id(int(bid))
                                except Exception:
                                    pass
                                # 4) Clear selection/hover
                                try:
                                    vmodel.selected_building_id = None
                                    vmodel.hovered_building_id = None
                                except Exception:
                                    pass
                                return True
                        # Split handle drag start
                        if split_rect is not None and pygame.Rect(split_rect).collidepoint(mx, my):
                            try:
                                self.model.split_drag_active = True
                                self.model.split_drag_bid = int(sel_bid)
                                # Suppress gameplay input while dragging split
                                if hasattr(world, 'state'):
                                    setattr(world.state, 'spawner_input_suppressed', True)
                            except Exception:
                                self.model.split_drag_active = False
                                self.model.split_drag_bid = None
                            return True
                        # Default (reset size) handle
                        if rst_rect is not None and pygame.Rect(rst_rect).collidepoint(mx, my):
                            try:
                                ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
                            except Exception:
                                ob = None
                            if ob is not None:
                                # Reset live entity size
                                try:
                                    ob.reset_to_original_size()
                                except Exception:
                                    pass
                                # Persist: drop overrides.scale for this building id
                                try:
                                    data = svc_load_buildings_instances()
                                except Exception:
                                    data = []
                                changed = False
                                for e in data or []:
                                    try:
                                        if int(e.get('id')) != int(sel_bid):
                                            continue
                                    except Exception:
                                        continue
                                    ov = e.get('overrides') or {}
                                    if isinstance(ov, dict) and 'scale' in ov:
                                        try:
                                            ov.pop('scale', None)
                                            # If overrides becomes empty, optionally drop it
                                            if not ov:
                                                try:
                                                    e.pop('overrides', None)
                                                except Exception:
                                                    e['overrides'] = {}
                                            else:
                                                e['overrides'] = ov
                                            changed = True
                                        except Exception:
                                            pass
                                    break
                                if changed:
                                    try:
                                        svc_write_buildings_instances(data)
                                    except Exception:
                                        pass
                                return True
                        # Z bottom/top tool buttons
                        if any(r is not None for r in (zb_minus, zb_plus, zt_minus, zt_plus)):
                            # Update ob.z_bottom / ob.z_top and persist overrides
                            try:
                                ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
                            except Exception:
                                ob = None
                            consumed = False
                            if ob is not None:
                                try:
                                    if zb_minus is not None and pygame.Rect(zb_minus).collidepoint(mx, my):
                                        ob.z_bottom = max(0, ob.z_bottom - 1)
                                        if ob.z_top < ob.z_bottom:
                                            ob.z_top = ob.z_bottom
                                        consumed = True
                                    elif zb_plus is not None and pygame.Rect(zb_plus).collidepoint(mx, my):
                                        ob.z_bottom = ob.z_bottom + 1
                                        if ob.z_top < ob.z_bottom:
                                            ob.z_top = ob.z_bottom
                                        consumed = True
                                    elif zt_minus is not None and pygame.Rect(zt_minus).collidepoint(mx, my):
                                        ob.z_top = max(ob.z_bottom, ob.z_top - 1)
                                        consumed = True
                                    elif zt_plus is not None and pygame.Rect(zt_plus).collidepoint(mx, my):
                                        ob.z_top = max(ob.z_bottom, ob.z_top + 1)
                                        consumed = True
                                except Exception:
                                    consumed = False
                            if consumed:
                                # Persist z_bottom/z_top overrides for this building id
                                try:
                                    data = svc_load_buildings_instances()
                                except Exception:
                                    data = []
                                for e in data or []:
                                    try:
                                        if int(e.get('id')) != int(sel_bid):
                                            continue
                                    except Exception:
                                        continue
                                    ov = e.get('overrides') or {}
                                    if not isinstance(ov, dict):
                                        ov = {}
                                    try:
                                        ov['z_bottom'] = int(getattr(ob, 'z_bottom', 0))
                                    except Exception:
                                        pass
                                    try:
                                        ov['z_top'] = int(getattr(ob, 'z_top', 0))
                                    except Exception:
                                        pass
                                    e['overrides'] = ov
                                    try:
                                        svc_write_buildings_instances(data)
                                    except Exception:
                                        pass
                                    break
                                return True
                        # Resize handle
                        if rz_rect is not None and pygame.Rect(rz_rect).collidepoint(mx, my):
                            # Begin resize drag
                            try:
                                ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
                            except Exception:
                                ob = None
                            if ob is not None and getattr(ob, 'image', None) is not None:
                                self.model.resizing_visual = True
                                try:
                                    self.model.resizing_visual_bid = int(sel_bid)
                                except Exception:
                                    self.model.resizing_visual_bid = None
                                self.model.resize_origin_mouse = (int(mx), int(my))
                                try:
                                    self.model.resize_start_size = tuple(ob.image.get_size())
                                except Exception:
                                    self.model.resize_start_size = None
                                # Suppress gameplay input while resizing
                                try:
                                    if hasattr(world, 'state'):
                                        setattr(world.state, 'spawner_input_suppressed', True)
                                except Exception:
                                    pass
                                return True
                    ob = None
                    try:
                        ob = ip.visuals.pick_visual_building_under_cursor(int(mx), int(my))
                    except Exception:
                        ob = None
                    if ob is not None:
                        try:
                            bid = getattr(ob, 'id', None)
                            if bid is not None:
                                ip.visuals.model.selected_building_id = int(bid)
                                # Consume to prevent gameplay from reacting
                                return True
                        except Exception:
                            pass
                    # Clicked away: clear selection
                    try:
                        ip.visuals.model.selected_building_id = None
                    except Exception:
                        pass
                    # Do not consume so other UI (e.g., panels) can still react if needed
            except Exception:
                pass

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

        # LMB up: finish resize
        if event.type == pygame.MOUSEBUTTONUP and event.button == 1 and getattr(self.model, 'resizing_visual', False):
            self.model.resizing_visual = False
            bid = getattr(self.model, 'resizing_visual_bid', None)
            self.model.resizing_visual_bid = None
            # Re-enable gameplay input
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            if bid is not None:
                try:
                    # Persist new size to buildings_instances.json as overrides.scale
                    data = svc_load_buildings_instances()
                except Exception:
                    data = []
                changed = False
                for e in data or []:
                    try:
                        if int(e.get('id')) != int(bid):
                            continue
                    except Exception:
                        continue
                    # Infer current size from the world entity
                    try:
                        ip = getattr(self.controller, 'instance_properties', None)
                        ob = ip.visuals._find_building_entity_by_id(int(bid))
                        cur_w, cur_h = ob.image.get_size()
                    except Exception:
                        cur_w = cur_h = None
                    if cur_w is not None and cur_h is not None:
                        ov = e.get('overrides') or {}
                        if not isinstance(ov, dict):
                            ov = {}
                        ov['scale'] = [int(cur_w), int(cur_h)]
                        e['overrides'] = ov
                        changed = True
                        break
                if changed:
                    try:
                        svc_write_buildings_instances(data)
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
