from __future__ import annotations

from typing import Any, Dict
import logging

from roguelike_engine.buildings.factory import build_from_config
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances,
    get_template_image_path,
)

from . import world as world_svc

logger = logging.getLogger(__name__)


def ensure_building_loaded(controller, bid: int) -> None:
    """If building with id 'bid' is not present in world.buildings, load it
    from instances/templates and append it. Editor-only best-effort.
    """
    world = world_svc.get_world(controller)
    if world is None:
        return
    # Already loaded in world?
    try:
        for ob in getattr(world, 'buildings', []) or []:
            try:
                if int(getattr(ob, 'id', -1)) == int(bid):
                    return
            except (TypeError, ValueError):
                continue
    except AttributeError:
        pass

    # Lookup in JSON for this building id
    inst_entry: Dict[str, Any] | None = None
    try:
        for e in load_buildings_instances():
            try:
                if int(e.get('id')) == int(bid):
                    inst_entry = e
                    break
            except (TypeError, ValueError, AttributeError):
                continue
    except Exception:
        inst_entry = None
    if not inst_entry:
        return

    # Build config for factory
    cfg: dict[str, Any] = {}
    try:
        cfg['image_path'] = get_template_image_path(int(inst_entry.get('template_id')))
        cfg['rel_x'] = int(inst_entry.get('rel_x', 0) or 0)
        cfg['rel_y'] = int(inst_entry.get('rel_y', 0) or 0)
        if inst_entry.get('zone') is not None:
            cfg['zone'] = str(inst_entry.get('zone'))
        ov = inst_entry.get('overrides') or {}
        if isinstance(ov, dict):
            if isinstance(ov.get('scale'), (list, tuple)) and len(ov.get('scale')) == 2:
                cfg['scale'] = (int(ov['scale'][0]), int(ov['scale'][1]))
            if 'z_bottom' in ov:
                cfg['z_bottom'] = int(ov['z_bottom'])
            if 'z_top' in ov:
                cfg['z_top'] = int(ov['z_top'])
    except (TypeError, ValueError, AttributeError):
        pass

    if not cfg.get('image_path'):
        return

    # Create Building and append to world
    try:
        cam = getattr(controller.parent.game, 'camera', None)
        b = build_from_config(cfg, camera=cam)
        try:
            setattr(b, 'id', int(bid))
        except (TypeError, ValueError, AttributeError):
            pass
        try:
            setattr(b, 'visible', True)
            setattr(b, 'editor_hidden', False)
            setattr(b, 'runtime_hidden', False)
        except AttributeError:
            pass
        try:
            if not hasattr(world, 'buildings') or world.buildings is None:
                setattr(world, 'buildings', [])
            world.buildings.append(b)
        except AttributeError:
            pass
        try:
            ents = getattr(controller.parent.game, 'entities', None)
            if ents is not None and hasattr(ents, 'buildings') and ents.buildings is not None:
                ents.buildings.append(b)
        except AttributeError:
            pass
    except Exception:
        logger.debug("building_loader.ensure_building_loaded: failed to build/append", exc_info=True)
        pass
