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
from roguelike_editors.spawner.services.persistence import load_spawners_json


class SpawnerEditorEventHandler:
    """Event handler for Spawner Editor.

    Responsibilities:
    - Maintain visibility flag.
    - Handle RMB drag to reposition a spawner's anchor tile.
    - Handle RMB panning of the camera when not on a spawner.
    - Suppress gameplay input while dragging/panning.
    """

    def __init__(self, controller: 'SpawnerEditorController'):
        self.controller = controller
        self.model = controller.model
        self.font = controller.font
        self.game = controller.game
        # Camera panning state (RMB when not hovering a spawner)
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

        # Placement mode: cancel with ESC
        if event.type == pygame.KEYDOWN and getattr(self.model, 'placing_template_id', None):
            if event.key == pygame.K_ESCAPE:
                self.model.placing_template_id = None
                # Re-enable gameplay input when leaving placement
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
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
            # Persist instance
            try:
                instances = load_instances_json()
            except Exception:
                instances = []
            new_entry = {
                'template_id': tpl_id,
                'zone': zone,
                'tile': [local_tx, local_ty],
            }
            instances.append(new_entry)
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
            # Exit placement mode and re-enable input
            self.model.placing_template_id = None
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass
            return True

        # RMB down: start drag if clicking near a spawner anchor; otherwise start camera panning
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            mx, my = event.pos
            # Only consume RMB if hovering a spawner
            eid = self.model.hovered_eid or pick_spawner_under_cursor(world, camera, mx, my)
            if eid is not None:
                self.model.dragging = True
                self.model.dragging_eid = eid
                # Suppress gameplay input (dash/spells) while dragging
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', True)
                except Exception:
                    pass
                # Snapshot original instance entry for persistence
                try:
                    cfg = world.components['SpawnerConfig'][eid]
                    zone = cfg.zone
                    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                    cur_tx, cur_ty = cfg.anchor_tile
                    local_start = (int(cur_tx - off_x), int(cur_ty - off_y))
                    tpl_id = cfg.template_id
                    _, idx, overrides = find_instance_in_json(tpl_id, zone, local_start)
                    self._drag_start_entry = {
                        'template_id': tpl_id,
                        'zone': zone,
                        'local_tile': local_start,
                        'overrides': overrides,
                        'index': idx,
                    }
                except Exception:
                    self._drag_start_entry = None
                return True
            # Not hovering: start camera panning with RMB
            self.panning = True
            self.pan_start = (mx, my)
            self.pan_offset_start = (camera.offset_x, camera.offset_y)
            # Suppress gameplay input during panning as well
            try:
                if hasattr(world, 'state'):
                    setattr(world.state, 'spawner_input_suppressed', True)
            except Exception:
                pass
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
                            # Keep input suppressed until decision
                            return True
                        # Otherwise persist directly
                        persist_drop(world, eid, self._drag_start_entry)
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
            # Stop camera panning if active
            if self.panning:
                self.panning = False
                # Re-enable gameplay input after panning ends
                try:
                    if hasattr(world, 'state'):
                        setattr(world.state, 'spawner_input_suppressed', False)
                except Exception:
                    pass
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

        return False
