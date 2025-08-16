from __future__ import annotations

import math
import os
import json
import pygame
from typing import Optional

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config import config
from roguelike_engine.config.map_config import global_map_settings
from roguelike_editors.spawner.spawner_editor_model import SpawnerEditorModel


class SpawnerEditorController:
    """Minimal controller for Spawner Editor.

    Responsibilities:
    - Maintain visibility flag.
    - Handle RMB drag to reposition a spawner's anchor tile.
    - Optionally render small hints (omitted for now).
    """

    def __init__(self, font: Optional[pygame.font.Font] = None):
        self.model = SpawnerEditorModel()
        self.font = font
        self.game = None  # set via set_game
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
            self.model.hovered_eid = self._pick_spawner_under_cursor(world, camera, mx, my)
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
            eid = self.model.hovered_eid or self._pick_spawner_under_cursor(world, camera, mx, my)
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
                    inst_list, idx, overrides = self._find_instance_in_json(tpl_id, zone, local_start)
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
                        self._persist_drop(world, eid)
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
            tx, ty = self._screen_to_tile(camera, mx, my)
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

    def render(self, screen: pygame.Surface) -> None:
        # Optional: draw small hint when visible
        if not self.model.visible or not self.font:
            return
        txt = self.font.render("Spawner Editor (RMB drag to move)", True, (0, 200, 255))
        screen.blit(txt, (10, 10))

    # Helpers -----------------------------------------------------------------
    def _pick_spawner_under_cursor(self, world, camera, mx: int, my: int) -> Optional[int]:
        comps = world.components
        if 'SpawnerConfig' not in comps:
            return None
        best_eid = None
        best_d2 = 999999
        # Hit radius in pixels (screen space)
        hit_r = 12
        for eid in world.get_entities_with('SpawnerConfig'):
            cfg = comps['SpawnerConfig'][eid]
            tx, ty = cfg.anchor_tile
            px = tx * TILE_SIZE + TILE_SIZE // 2
            py = ty * TILE_SIZE + TILE_SIZE // 2
            sx, sy = camera.apply((px, py))
            dx, dy = mx - sx, my - sy
            d2 = dx*dx + dy*dy
            if d2 <= hit_r*hit_r and d2 < best_d2:
                best_d2 = d2
                best_eid = eid
        return best_eid

    def _screen_to_tile(self, camera, sx: int, sy: int) -> tuple[int, int]:
        # Invert Camera.apply: screen = (world - offset) * zoom
        zoom = getattr(camera, 'zoom', 1.0) or 1.0
        wx = sx / zoom + camera.offset_x
        wy = sy / zoom + camera.offset_y
        tx = int(math.floor(wx / TILE_SIZE))
        ty = int(math.floor(wy / TILE_SIZE))
        return tx, ty

    # Persistence helpers -----------------------------------------------------
    def _instances_path(self) -> str:
        base = getattr(config, 'DATA_DIR', 'data')
        return os.path.join(base, 'spawners', 'instances.json')

    def _load_instances_json(self) -> list[dict]:
        path = self._instances_path()
        try:
            with open(path, 'r', encoding='utf-8') as f:
                data = json.load(f)
            return data if isinstance(data, list) else []
        except FileNotFoundError:
            return []

    def _write_instances_json(self, data: list[dict]) -> None:
        path = self._instances_path()
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)

    def _find_instance_in_json(self, template_id: str, zone: str, local_tile: tuple[int, int]) -> tuple[list[dict], Optional[int], Optional[dict]]:
        """Load JSON and find the instance matching template_id, zone and tile=local_tile.
        Returns (instances_list, index or None, overrides or None).
        """
        data = self._load_instances_json()
        idx_found = None
        overrides = None
        for i, inst in enumerate(data):
            try:
                if inst.get('template_id') == template_id and inst.get('zone') == zone:
                    tile = inst.get('tile', [0, 0])
                    if tuple(tile) == tuple(local_tile):
                        idx_found = i
                        overrides = inst.get('overrides')
                        break
            except Exception:
                continue
        return data, idx_found, overrides

    def _persist_drop(self, world, eid: int) -> None:
        # Compute new local tile from cfg.anchor_tile and zone offsets
        comps = world.components
        if 'SpawnerConfig' not in comps or eid not in comps['SpawnerConfig']:
            return
        cfg = comps['SpawnerConfig'][eid]
        zone = cfg.zone
        off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
        tx, ty = cfg.anchor_tile
        new_local = (int(tx - off_x), int(ty - off_y))
        tpl_id = cfg.template_id

        data, idx_found, _ = self._find_instance_in_json(
            tpl_id,
            zone,
            # Prefer original local tile if we captured it; else try to locate by new tile
            tuple(self._drag_start_entry['local_tile']) if self._drag_start_entry and self._drag_start_entry.get('local_tile') else new_local,
        )
        # If we didn't find by original, try by new (handle pre-existing move or missing snapshot)
        if idx_found is None:
            _, idx_found, _ = self._find_instance_in_json(tpl_id, zone, new_local)

        entry = {
            'template_id': tpl_id,
            'zone': zone,
            'tile': [int(new_local[0]), int(new_local[1])],
        }
        # Preserve overrides if we have snapshot or existing
        overrides = None
        if self._drag_start_entry and self._drag_start_entry.get('overrides') is not None:
            overrides = self._drag_start_entry['overrides']
        elif idx_found is not None:
            try:
                overrides = data[idx_found].get('overrides')
            except Exception:
                overrides = None
        if overrides is not None:
            entry['overrides'] = overrides

        if idx_found is not None:
            data[idx_found] = entry
        else:
            data.append(entry)

        self._write_instances_json(data)
        # Clear snapshot after persisting
        self._drag_start_entry = None
