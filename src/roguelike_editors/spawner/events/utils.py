from __future__ import annotations

from typing import Any, Optional
import logging
import pygame


def safe_get_world(game: Any) -> Optional[Any]:
    try:
        ecs = getattr(game, 'ecs', None)
        return getattr(ecs, 'ecs_world', None)
    except AttributeError:
        return None


def safe_get_camera(game: Any) -> Optional[Any]:
    try:
        return getattr(game, 'camera', None)
    except AttributeError:
        return None


def find_building_in_world_by_id(world: Any, bid: int) -> Optional[Any]:
    if not world:
        return None
    try:
        for ob in getattr(world, 'buildings', []) or []:
            try:
                if int(getattr(ob, 'id', None)) == int(bid):
                    return ob
            except (TypeError, ValueError):
                continue
    except AttributeError:
        pass
    return None


def log_info_safe(logger: logging.Logger, msg: str, *args) -> None:
    try:
        logger.info(msg, *args)
    except (TypeError, ValueError):
        pass


def compute_spawner_handle_rects(camera: Any, building: Any) -> dict[str, Optional[pygame.Rect]]:
    """Compute Delete/Reset/Resize handle rects in screen-space for a given building.
    Mirrors SpawnerEditorView drawing logic.
    Returns a dict with keys: 'delete', 'reset', 'resize'. Any may be None on error.
    """
    try:
        bx, by = camera.apply((getattr(building, 'x', 0), getattr(building, 'y', 0)))
        bw, bh = camera.scale(building.image.get_size())
    except (AttributeError, TypeError):
        return {'delete': None, 'reset': None, 'resize': None}


def get_selected_building_id(ip_controller: Any) -> Optional[int]:
    """Return the selected building id from InstanceProperties' VisualsModel.

    Centralizes the source of truth so event modules don't diverge. Returns an
    int or None if unavailable.
    """
    try:
        vmodel = getattr(getattr(ip_controller, 'visuals', None), 'model', None) if ip_controller else None
        bid = getattr(vmodel, 'selected_building_id', None) if vmodel else None
        return int(bid) if bid is not None else None
    except (AttributeError, TypeError, ValueError):
        return None
    try:
        handle_size = max(15, min(65, int(bw * 0.10)))
    except (TypeError, ValueError):
        handle_size = 25
    try:
        del_rect = pygame.Rect(int(bx + bw - 3 * handle_size), int(by), int(handle_size), int(handle_size))
        rst_rect = pygame.Rect(int(bx + bw - 2 * handle_size), int(by), int(handle_size), int(handle_size))
        rz_rect = pygame.Rect(int(bx + bw - 1 * handle_size), int(by), int(handle_size), int(handle_size))
        return {'delete': del_rect, 'reset': rst_rect, 'resize': rz_rect}
    except (TypeError, ValueError, pygame.error):
        return {'delete': None, 'reset': None, 'resize': None}
