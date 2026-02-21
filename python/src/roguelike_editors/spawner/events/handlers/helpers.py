from __future__ import annotations

import logging
import pygame

from .. import confirmations as conf
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as svc_load_buildings_instances,
    write_buildings_instances as svc_write_buildings_instances,
)


def handle_keydown(h, ctx, event: pygame.event.Event) -> bool:
    """Route KEYDOWN events to pending confirmation flows."""
    try:
        if event.type == pygame.KEYDOWN and getattr(h.model, 'pending_zone_confirm', None):
            if conf.handle_zone_confirm(ctx, event):
                return True
        if event.type == pygame.KEYDOWN and getattr(h.model, 'pending_delete_confirm', None):
            if conf.handle_delete_confirm(ctx, event):
                return True
    except Exception:
        logging.getLogger(__name__).debug("handle_keydown: confirmation handlers failed", exc_info=True)
    return False


def reset_selected_building_size(h, sel_bid: int | None) -> bool:
    logger = logging.getLogger(__name__)
    if sel_bid is None:
        return False
    try:
        ip = getattr(h.controller, 'instance_properties', None)
        ob = None
        try:
            if ip is not None and hasattr(ip, 'visuals'):
                ob = ip.visuals._find_building_entity_by_id(int(sel_bid))
        except (AttributeError, TypeError, ValueError):
            ob = None
        if ob is not None:
            try:
                ob.reset_to_original_size()
            except AttributeError:
                logger.debug("_reset_selected_building_size: failed to reset entity size", exc_info=True)
            # Persist: drop overrides.scale
            try:
                data = svc_load_buildings_instances()
            except OSError:
                data = []
            changed = False
            for e in data or []:
                try:
                    if int(e.get('id')) != int(sel_bid):
                        continue
                except (TypeError, ValueError):
                    continue
                ov = e.get('overrides') or {}
                if isinstance(ov, dict) and 'scale' in ov:
                    try:
                        ov.pop('scale', None)
                        if not ov:
                            try:
                                e.pop('overrides', None)
                            except KeyError:
                                logger.debug("_reset_selected_building_size: failed to pop overrides; setting empty dict", exc_info=True)
                                e['overrides'] = {}
                        else:
                            e['overrides'] = ov
                        changed = True
                    except (AttributeError, KeyError, TypeError):
                        logger.debug("_reset_selected_building_size: failed updating overrides dict", exc_info=True)
                break
            if changed:
                try:
                    svc_write_buildings_instances(data)
                except OSError:
                    logger.debug("_reset_selected_building_size: failed persisting buildings_instances after reset", exc_info=True)
            # Also remove any per-visuals scale stored under spawners_instances.json for this selected instance
            try:
                from roguelike_editors.spawner.services.persistence import load_instances_json as _sp_load, write_instances_json as _sp_write
            except Exception:
                _sp_load = _sp_write = None            
            if _sp_load is not None and _sp_write is not None:
                inst_list = _sp_load()
                changed_vis = False
                sel_inst = getattr(getattr(h.controller.instance_properties, 'model', None), 'selected_instance', None)
                target_id = str(sel_inst.get('id')) if isinstance(sel_inst, dict) and sel_inst.get('id') is not None else None
                if target_id is not None:
                    for inst in inst_list or []:
                        try:
                            if str(inst.get('id')) != target_id:
                                continue
                            vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                            for k, v in list(vis.items()):
                                try:
                                    if isinstance(v, dict):
                                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                    else:
                                        vid = int(v)
                                except Exception:
                                    vid = None
                                if vid is not None and int(vid) == int(sel_bid):
                                    if isinstance(v, dict) and 'scale' in v:
                                        vv = dict(v)
                                        try:
                                            vv.pop('scale', None)
                                        except Exception:
                                            pass
                                        vis[k] = vv
                                        inst['visuals'] = vis
                                        changed_vis = True
                            break
                        except Exception:
                            continue
                    if changed_vis:
                        try:
                            _sp_write(inst_list)
                        except OSError:
                            logger.debug("_reset_selected_building_size: failed persisting spawners_instances visuals after reset", exc_info=True)
                # Update in-memory mapping as well
                try:
                    if isinstance(h.controller.instance_properties.model, object) and isinstance(getattr(h.controller.instance_properties.model, 'visuals', None), dict):
                        vm = dict(h.controller.instance_properties.model.visuals)
                        for k, v in list(vm.items()):
                            try:
                                if isinstance(v, dict):
                                    vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                else:
                                    vid = int(v)
                            except Exception:
                                vid = None
                            if vid is not None and int(vid) == int(sel_bid) and isinstance(v, dict) and 'scale' in v:
                                vv = dict(v)
                                try:
                                    vv.pop('scale', None)
                                except Exception:
                                    pass
                                vm[k] = vv
                        h.controller.instance_properties.model.visuals = vm
                        if isinstance(h.controller.instance_properties.model.selected_instance, dict):
                            h.controller.instance_properties.model.selected_instance['visuals'] = vm
                except Exception:
                    logger.debug("_reset_selected_building_size: failed updating in-memory visuals map after reset", exc_info=True)
        return True
    except (AttributeError, TypeError, ValueError):
        logger.debug("_reset_selected_building_size: unexpected error", exc_info=True)
    return False
