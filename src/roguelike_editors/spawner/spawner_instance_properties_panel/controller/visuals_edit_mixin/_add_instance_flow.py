from __future__ import annotations

from typing import Any, Optional
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_templates as svc_load_buildings_templates,
    get_template_image_path as svc_get_template_image_path,
)


def add_building_instance_for_visual_flow(owner: Any, state_key: str, reveal: bool = True) -> Optional[int]:
    """Create or reuse a building instance for a given visual state.

    It follows the original behavior:
    - Prefer current inline text if editing this state.
    - Validate template id.
    - Try to reuse an existing instance in same zone/rel position/template.
    - Otherwise, create a new instance centered on the spawner and persist.
    - Update visuals mapping, persist spawner instance, refresh rows/indexes, and optionally reveal.
    """
    txt = (getattr(owner.model, 'visuals_pending_templates', None) or {}).get(state_key, '')
    if getattr(owner.model, 'visuals_editing_state', None) == state_key:
        vti = getattr(owner.visuals.model, 'text_input', None)
        if vti is not None:
            try:
                txt = vti.text
            except AttributeError:
                pass

    ok, _msg, tpl_id = owner._validate_template_text(txt)
    if tpl_id is None or not ok:
        return None

    data = owner._load_buildings_instances()
    next_id = 1
    try:
        ids = [int(e.get('id')) for e in data if e.get('id') is not None]
        if ids:
            next_id = max(ids) + 1
    except Exception:
        pass

    zone: Optional[str] = None
    local_tile = (0, 0)
    try:
        zone = str((getattr(owner.model, 'selected_instance', {}) or {}).get('zone'))
    except (AttributeError, TypeError, ValueError):
        zone = None
    try:
        t = (getattr(owner.model, 'selected_instance', {}) or {}).get('tile', (0, 0))
        if isinstance(t, (list, tuple)) and len(t) >= 2:
            local_tile = (int(t[0]), int(t[1]))
    except (AttributeError, TypeError, ValueError):
        local_tile = (0, 0)
    if not zone:
        zone = 'lobby'

    try:
        rel_x = int(local_tile[0] * TILE_SIZE)
        rel_y = int(local_tile[1] * TILE_SIZE)
    except (TypeError, ValueError):
        rel_x = 0
        rel_y = 0

    # Try reuse
    try:
        zone_norm = zone
        desired_tid = int(tpl_id)
        best_id: Optional[int] = None
        best_score = (-1, -1)
        for e in data:
            try:
                if int(e.get('template_id')) != desired_tid:
                    continue
                if str(e.get('zone') or 'lobby') != str(zone_norm):
                    continue
                if int(e.get('rel_x') or 0) != int(local_tile[0] * TILE_SIZE):
                    continue
                if int(e.get('rel_y') or 0) != int(local_tile[1] * TILE_SIZE):
                    continue
                ov = e.get('overrides') if isinstance(e, dict) else {}
                tied = 0
                try:
                    sid = str((getattr(owner.model, 'selected_instance', {}) or {}).get('id')) if (getattr(owner.model, 'selected_instance', {}) or {}).get('id') is not None else None
                    if sid and (str(e.get('spawner_instance_id')) == sid or str((ov or {}).get('spawner_instance_id')) == sid):
                        tied = 1
                except (AttributeError, TypeError, ValueError):
                    tied = 0
                is_tag = 1 if (isinstance(ov, dict) and bool(ov.get('_is_spawner_visual'))) else 0
                score = (tied, is_tag)
                if score > best_score:
                    best_score = score
                    best_id = int(e.get('id'))
            except (AttributeError, TypeError, ValueError):
                continue
        if best_id is not None:
            visuals = getattr(owner.model, 'visuals', {}) or {}
            key_map = getattr(owner.model, 'visuals_key_map', {}) or {}
            json_key = key_map.get(state_key, state_key)
            visuals[json_key] = {'instance_id': best_id, 'template_id': int(tpl_id)}
            owner.model.visuals = visuals
            try:
                if owner.model.selected_instance is not None:
                    owner.model.selected_instance['visuals'] = visuals
            except AttributeError:
                pass
            owner._persist_instance()
            owner._building_index = None
            owner._ensure_buildings_index()
            owner._build_visuals_rows()
            if reveal:
                try:
                    owner._tag_and_reveal_building(int(best_id), state_key)
                except (AttributeError, TypeError, ValueError):
                    pass
            return best_id
    except (AttributeError, TypeError, ValueError):
        pass

    try:
        owner._log.debug(
            f"[InstanceProps] No reusable instance found -> creating new (zone={zone}, tile={local_tile}, tpl={tpl_id})"
        )
    except (AttributeError, TypeError, ValueError):
        pass

    # Center new building on spawner center using scaled image bounding rect
    try:
        spawn_cx = int(rel_x + (TILE_SIZE // 2))
        spawn_cy = int(rel_y + (TILE_SIZE // 2))
        w: Optional[int] = None
        h: Optional[int] = None
        brx = bry = 0
        brw = brh = None
        anchor_mode = 'content_center'
        try:
            for tentry in svc_load_buildings_templates():
                try:
                    if int(tentry.get('id')) == int(tpl_id):
                        oscale = tentry.get('original_scale')
                        try:
                            am = str(tentry.get('anchor_mode') or '')
                            if am:
                                anchor_mode = am
                        except (AttributeError, TypeError, ValueError):
                            pass
                        if isinstance(oscale, (list, tuple)) and len(oscale) >= 2:
                            w = int(oscale[0])
                            h = int(oscale[1])
                            try:
                                img_path = svc_get_template_image_path(int(tpl_id))
                                if img_path:
                                    import pygame as _pg
                                    raw = _pg.image.load(img_path)
                                    surf = _pg.transform.scale(raw, (int(w), int(h)))
                                    br = surf.get_bounding_rect(min_alpha=1)
                                    brx, bry, brw, brh = br.x, br.y, br.w, br.h
                            except (AttributeError, TypeError, ValueError, pygame.error):
                                brw = brh = None
                        break
                except (AttributeError, TypeError, ValueError):
                    continue
        except (AttributeError, TypeError, ValueError, OSError):
            pass
        if w is None or h is None:
            try:
                img_path = svc_get_template_image_path(int(tpl_id))
                if img_path:
                    import pygame as _pg
                    raw = _pg.image.load(img_path)
                    iw, ih = raw.get_size()
                    if iw > 512 or ih > 512:
                        iw //= 4
                        ih //= 4
                    w, h = int(iw), int(ih)
                    try:
                        surf = _pg.transform.scale(raw, (int(w), int(h)))
                        br = surf.get_bounding_rect(min_alpha=1)
                        brx, bry, brw, brh = br.x, br.y, br.w, br.h
                    except (AttributeError, TypeError, ValueError, pygame.error):
                        brw = brh = None
            except (AttributeError, TypeError, ValueError, OSError, pygame.error):
                w = None
                h = None
        if w is not None and h is not None and w > 0 and h > 0:
            if anchor_mode == 'base_center' and brw is not None and brh is not None and brw > 0 and brh > 0:
                rel_x = int(spawn_cx - (brx + brw // 2))
                rel_y = int(spawn_cy - (bry + brh))
            elif brw is not None and brh is not None and brw > 0 and brh > 0 and anchor_mode == 'content_center':
                rel_x = int(spawn_cx - (brx + brw // 2))
                rel_y = int(spawn_cy - (bry + brh // 2))
            else:
                rel_x = int(spawn_cx - (w // 2))
                rel_y = int(spawn_cy - (h // 2))
    except (AttributeError, TypeError, ValueError):
        pass

    entry = {
        'id': next_id,
        'template_id': tpl_id,
        'zone': zone,
        'rel_x': int(rel_x),
        'rel_y': int(rel_y),
    }
    try:
        if isinstance(owner.model.selected_instance, dict):
            sid = (
                str(owner.model.selected_instance.get('id'))
                if owner.model.selected_instance.get('id') is not None
                else None
            )
        else:
            sid = None
        entry['overrides'] = entry.get('overrides') or {}
        entry['overrides']['_is_spawner_visual'] = True
        if sid:
            entry['overrides']['spawner_instance_id'] = sid
        try:
            if 'overrides' in entry:
                if (
                    'w' in locals() and 'h' in locals() and locals().get('w') is not None and locals().get('h') is not None
                    and int(locals()['w']) > 0 and int(locals()['h']) > 0
                ):
                    entry['overrides']['scale'] = [int(locals()['w']), int(locals()['h'])]
        except (AttributeError, TypeError, ValueError):
            pass
        try:
            if sid:
                entry['spawn_id'] = str(sid)
                entry['spawner_instance_id'] = str(sid)
        except (AttributeError, TypeError, ValueError):
            pass
    except (AttributeError, TypeError, ValueError):
        pass

    data.append(entry)
    owner._write_buildings_instances(data)

    try:
        try:
            owner.visuals._ensure_building_loaded(int(next_id))
        except (AttributeError, TypeError, ValueError):
            pass
        ob = None
        try:
            ob = owner.visuals._find_building_entity_by_id(int(next_id))
        except (AttributeError, TypeError, ValueError):
            ob = None
        if ob is not None:
            surf = getattr(getattr(ob, 'model', None), 'image', None)
            br = None
            try:
                if surf is not None:
                    br = surf.get_bounding_rect(min_alpha=1)
            except (AttributeError, TypeError, ValueError):
                br = None
            if br is not None and br.w > 0 and br.h > 0:
                spawn_cx = int((local_tile[0] * TILE_SIZE) + (TILE_SIZE // 2))
                spawn_cy = int((local_tile[1] * TILE_SIZE) + (TILE_SIZE // 2))
                corr_rx = int(spawn_cx - (br.x + br.w // 2))
                corr_ry = int(spawn_cy - (br.y + br.h // 2))
                for e in data:
                    try:
                        if int(e.get('id')) == int(next_id):
                            e['rel_x'] = corr_rx
                            e['rel_y'] = corr_ry
                            break
                    except (AttributeError, TypeError, ValueError):
                        continue
                owner._write_buildings_instances(data)
                try:
                    setattr(getattr(ob, 'model', ob), 'rel_x', corr_rx)
                    setattr(getattr(ob, 'model', ob), 'rel_y', corr_ry)
                except (AttributeError, TypeError, ValueError):
                    pass
    except (AttributeError, TypeError, ValueError):
        pass

    visuals = getattr(owner.model, 'visuals', {}) or {}
    key_map = getattr(owner.model, 'visuals_key_map', {}) or {}
    json_key = key_map.get(state_key, state_key)
    visuals[json_key] = {'instance_id': next_id, 'template_id': int(tpl_id)}
    try:
        owner._log.debug(
            f"[InstanceProps] add_building_instance_for_visual: set visuals[{json_key}]={next_id}"
        )
    except (AttributeError, TypeError, ValueError):
        pass
    owner.model.visuals = visuals

    try:
        inst = owner.model.selected_instance
        if isinstance(inst, dict):
            ov = dict(inst.get('overrides') or {})
            ov['visible_in_game'] = True
            inst['overrides'] = ov
    except (AttributeError, TypeError, ValueError):
        pass
    try:
        if owner.model.selected_instance is not None:
            owner.model.selected_instance['visuals'] = visuals
    except AttributeError:
        pass
    try:
        owner._log.debug(
            f"[InstanceProps] add_building_instance_for_visual: model.visuals now={owner.model.visuals}"
        )
    except (AttributeError, TypeError, ValueError):
        pass

    owner._persist_instance()
    try:
        owner._reload_selected_from_json()
    except (AttributeError, OSError, ValueError, TypeError):
        pass

    owner._building_index = None
    owner._ensure_buildings_index()
    owner._build_visuals_rows()

    owner.model.visuals_editing_state = None
    if reveal:
        try:
            owner._tag_and_reveal_building(int(next_id), state_key)
        except (AttributeError, TypeError, ValueError):
            pass

    return next_id
