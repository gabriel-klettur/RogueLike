from __future__ import annotations

from typing import Optional
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.services import (
    pick_spawner_under_cursor,
    screen_to_tile,
    find_instance_in_json,
    persist_drop,
)


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
        # Stop drag when toggling off
        if not self.model.visible:
            self.model.dragging = False
            self.model.dragging_eid = None
            self.model.hovered_eid = None
            self.panning = False
            self._drag_start_entry = None
            try:
                world = getattr(self.game.ecs, 'ecs_world', None)
                if world and hasattr(world, 'state'):
                    setattr(world.state, 'spawner_editor_hovered_eid', None)
                    # Ensure input is re-enabled on hide
                    setattr(world.state, 'spawner_input_suppressed', False)
            except Exception:
                pass

    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.model.visible or not self.game:
            return False
        world = getattr(self.game.ecs, 'ecs_world', None)
        camera = getattr(self.game, 'camera', None)
        if not world or not camera:
            return False

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
                # Persist new position to instances.json
                try:
                    if eid is not None:
                        persist_drop(world, eid, self._drag_start_entry)
                except Exception:
                    pass
                # Re-enable gameplay input after drag ends
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

        return False
