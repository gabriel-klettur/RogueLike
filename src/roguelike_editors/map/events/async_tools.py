from __future__ import annotations

import logging
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_editor import TILE_PAINT_BATCH, TILE_PAINT_TICK
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.loader import get_sprite_for_tile
from roguelike_editors.map.services.overlay_service import set_overlay_cell, merge_zone_to_world

logger = logging.getLogger(__name__)


def process_async_tool(camera, state, controller, manager, map_manager) -> None:
    tool = state.executing_tool
    if tool == "paint_tiles":
        _handle_paint_tiles_execution(camera, state, controller, map_manager)
    elif tool == "clear_colliders":
        _handle_clear_colliders_execution(state, controller, manager)
    elif tool == "paint_colliders":
        _handle_paint_colliders_execution(state, controller, manager)


def _handle_paint_tiles_execution(camera, state, controller, map_manager) -> None:
    idx = state.execution_index
    zone = state.executing_zone
    if idx < state.execution_total:
        tile = state.execution_list[idx]
        _apply_tile_overlay(tile, state)
        _apply_ground_overlay(tile, state, map_manager)
        state.execution_index += 1
        try:
            tx = int(tile.x) // TILE_SIZE
            ty = int(tile.y) // TILE_SIZE
            map_manager.view.update_chunks(map_manager, camera, [(ty, tx)])
        except Exception:
            pass
        try:
            if len(state.dirty_cells) >= TILE_PAINT_BATCH or (
                state.execution_index % TILE_PAINT_TICK == 0 and state.dirty_cells
            ):
                cells = list(state.dirty_cells)
                map_manager.view.update_chunks(map_manager, camera, cells)
                state.dirty_cells.clear()
        except Exception:
            pass
        try:
            if (state.execution_index % 128) == 0:
                map_manager.view.invalidate_cache()
        except Exception:
            pass
        total = max(state.execution_total, 1)
        percent = int((state.execution_index / total) * 100)
        if percent >= (state.last_progress_report + 10):
            elapsed = pygame.time.get_ticks() - state.execution_start_time
            logger.debug(
                f"[MapEditor] Painting zone={zone} progress={percent}% "
                f"({state.execution_index}/{total}) elapsed={elapsed}ms"
            )
            state.last_progress_report = percent
    else:
        try:
            if state.dirty_cells:
                cells = list(state.dirty_cells)
                map_manager.view.update_chunks(map_manager, camera, cells)
                state.dirty_cells.clear()
        except Exception:
            pass
        try:
            _finalize_paint_tiles(zone, state, controller, map_manager)
        except Exception as e:
            logger.exception(f"[MapEditor] Error finalizing paint tiles for zone={zone}: {e}")
        finally:
            clear_async_state(state)
            try:
                setattr(state, "tutorial_paint_tiles_finalized_pulse", True)
            except Exception:
                pass


def _apply_tile_overlay(tile, state) -> None:
    orig = tile.tile_type
    tile.overlay_code = state.tile_code
    tile.sprite = get_sprite_for_tile(orig, tile.overlay_code)
    tile.scaled_cache.clear()


def _apply_ground_overlay(tile, state, map_manager) -> None:
    tx = tile.x // TILE_SIZE
    ty = tile.y // TILE_SIZE
    ground_layer = map_manager.tiles_by_layer.get(Layer.Ground)
    if ground_layer and 0 <= ty < len(ground_layer) and 0 <= tx < len(ground_layer[0]):
        gt = ground_layer[ty][tx]
        orig2 = gt.tile_type
        gt.overlay_code = tile.overlay_code
        gt.sprite = get_sprite_for_tile(orig2, gt.overlay_code)
        gt.scaled_cache.clear()
    world = map_manager.layers.get(Layer.Ground)
    before = None
    if world and 0 <= ty < len(world) and 0 <= tx < len(world[0]):
        before = world[ty][tx]
    set_overlay_cell(map_manager, tx, ty, tile.overlay_code)
    try:
        if before != tile.overlay_code and state.current_command is not None:
            state.current_command.add_edit(ty, tx, before, tile.overlay_code)
    except Exception:
        pass
    state.dirty_cells.add((ty, tx))


def _finalize_paint_tiles(zone: str, state, controller, map_manager) -> None:
    start = pygame.time.get_ticks()
    layers = controller.zones.load_layers(zone)
    off_x, off_y = global_map_settings.zone_offsets.get(zone)
    wz, hz = global_map_settings.zone_size
    grid = [["" for _ in range(wz)] for _ in range(hz)]
    for t in map_manager.tiles_by_zone.get(zone, []):
        lx = t.x // TILE_SIZE - off_x
        ly = t.y // TILE_SIZE - off_y
        if 0 <= lx < wz and 0 <= ly < hz:
            grid[ly][lx] = t.overlay_code
    painted = sum(1 for row in grid for code in row if code)
    layers[Layer.Ground] = grid
    controller.zones.save_layers(zone, layers)
    merge_zone_to_world(map_manager, zone, grid)
    elapsed = pygame.time.get_ticks() - start
    logger.info(
        f"[MapEditor] Overlay persisted for zone={zone} layer=Ground size={wz}x{hz} "
        f"painted_cells={painted} duration={elapsed}ms"
    )
    map_manager.view.invalidate_cache()
    if state.current_command is not None:
        state.undo_stack.append(state.current_command)
        state.redo_stack.clear()
        state.current_command = None
    try:
        setattr(state, "tutorial_paint_tiles_finalized_pulse", True)
    except Exception:
        pass


def perform_undo(camera, state, map_manager) -> None:
    if not state.undo_stack:
        return
    cmd = state.undo_stack.pop()
    try:
        cells = cmd.undo(map_manager)
        if cells:
            map_manager.view.update_chunks(map_manager, camera, cells)
    finally:
        state.redo_stack.append(cmd)
        try:
            setattr(state, "tutorial_undo_performed_pulse", True)
        except Exception:
            pass


def perform_redo(camera, state, map_manager) -> None:
    if not state.redo_stack:
        return
    cmd = state.redo_stack.pop()
    try:
        cells = cmd.redo(map_manager)
        if cells:
            map_manager.view.update_chunks(map_manager, camera, cells)
    finally:
        state.undo_stack.append(cmd)
        try:
            setattr(state, "tutorial_redo_performed_pulse", True)
        except Exception:
            pass


def _handle_clear_colliders_execution(state, controller, manager) -> None:
    idx = state.execution_index
    zone = state.executing_zone
    if idx < state.execution_total:
        state.execution_index += 1
    else:
        try:
            controller.toolbar.clear_colliders.finalize(zone)
        finally:
            manager.game.ecs.ecs_world.rebuild_spatial_index()
            clear_async_state(state)
            try:
                setattr(state, "tutorial_clear_colliders_finalized_pulse", True)
            except Exception:
                pass


def _handle_paint_colliders_execution(state, controller, manager) -> None:
    idx = state.execution_index
    zone = state.executing_zone
    if idx < state.execution_total:
        state.execution_index += 1
    else:
        try:
            controller.toolbar.paint_colliders.finalize(zone)
        finally:
            manager.game.ecs.ecs_world.rebuild_spatial_index()
            clear_async_state(state)
            try:
                setattr(state, "tutorial_paint_colliders_finalized_pulse", True)
            except Exception:
                pass


def clear_async_state(state) -> None:
    state.executing_tool = None
    state.executing_zone = None
    state.execution_list.clear()
    state.execution_index = 0
    state.execution_total = 0
