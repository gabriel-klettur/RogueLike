from __future__ import annotations

import logging
from typing import Any

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances,
)

from . import world as world_svc
from . import mapping as mapping_svc
from . import building_loader as loader_svc

logger = logging.getLogger(__name__)


def center_camera_on_state(controller, state_key: str) -> None:
    try:
        cam = getattr(controller.parent.game, 'camera', None)
        if cam is None:
            return None
        ob = world_svc.find_visual_entity_for_state(controller, state_key)
        if ob is not None:
            try:
                zone = getattr(ob, 'zone', None)
                if zone is None:
                    zone = getattr(getattr(ob, 'model', ob), 'zone', None)
                if not zone:
                    zone = 'lobby'
                rx = getattr(getattr(ob, 'model', ob), 'rel_x', None)
                ry = getattr(getattr(ob, 'model', ob), 'rel_y', None)
                if rx is None or ry is None:
                    raise RuntimeError('missing rel coords on entity')
                off = global_map_settings.zone_offsets.get(str(zone), (0, 0))
                bx = int(off[0] * TILE_SIZE) + int(rx)
                by = int(off[1] * TILE_SIZE) + int(ry)
                zoom = getattr(cam, 'zoom', 1.0) or 1.0
                cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
                cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
                return None
            except (AttributeError, TypeError, ValueError):
                logger.debug("camera.center_camera_on_state: failed live entity path", exc_info=True)
    except (AttributeError, TypeError, ValueError):
        logger.debug("camera.center_camera_on_state: unexpected error (live path)", exc_info=True)

    bid = mapping_svc.get_instance_id_for_state(controller, state_key)
    if bid is None:
        return None
    try:
        loader_svc.ensure_building_loaded(controller, int(bid))
    except Exception:
        pass
    try:
        cam = getattr(controller.parent.game, 'camera', None)
        if cam is None:
            return None
        bx = by = None
        bzone = 'lobby'
        for e in load_buildings_instances():
            try:
                if int(e.get('id')) == int(bid):
                    bzone = str(e.get('zone') or 'lobby')
                    rx = int(e.get('rel_x') or 0)
                    ry = int(e.get('rel_y') or 0)
                    off = global_map_settings.zone_offsets.get(bzone, (0, 0))
                    bx = int(off[0] * TILE_SIZE) + int(rx)
                    by = int(off[1] * TILE_SIZE) + int(ry)
                    break
            except Exception:
                continue
        if bx is not None and by is not None:
            zoom = getattr(cam, 'zoom', 1.0) or 1.0
            cam.offset_x = float(bx) - (cam.screen_width / (2 * zoom))
            cam.offset_y = float(by) - (cam.screen_height / (2 * zoom))
    except Exception:
        pass
    return None
