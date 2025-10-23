from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple
import logging
import pygame
import os
import json

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.buildings.building import Building
from roguelike_engine.config import config

from .buildings_repo import (
    load_buildings_instances_json,
    write_buildings_instances_json,
    load_buildings_templates_json,
    get_template_image_path,
)
from .loaders import load_instances

logger = logging.getLogger(__name__)


def calc_centered_rel(local_tile: Tuple[int, int], tpl_entry: Optional[dict], img_path: Optional[str]) -> Tuple[int, int, Optional[Tuple[int, int]]]:
    rel_x = int(local_tile[0] * TILE_SIZE)
    rel_y = int(local_tile[1] * TILE_SIZE)
    spawn_cx = int(rel_x + (TILE_SIZE // 2))
    spawn_cy = int(rel_y + (TILE_SIZE // 2))
    w = h = None
    try:
        if isinstance(tpl_entry, dict) and isinstance(tpl_entry.get('original_scale'), (list, tuple)):
            oscale = tpl_entry['original_scale']
            if len(oscale) >= 2:
                w, h = int(oscale[0]), int(oscale[1])
    except Exception:
        w = h = None
    br = None
    if img_path:
        try:
            surf = pygame.image.load(img_path)
            if w is not None and h is not None and w > 0 and h > 0:
                surf = pygame.transform.scale(surf, (int(w), int(h)))
            br = surf.get_bounding_rect(min_alpha=1)
        except Exception:
            br = None
            if w is None or h is None:
                try:
                    iw, ih = surf.get_size()  # type: ignore[name-defined]
                    w, h = int(iw), int(ih)
                except Exception:
                    w = h = None
    try:
        if br is not None and br.w > 0 and br.h > 0:
            rel_x = int(spawn_cx - (br.x + br.w // 2))
            rel_y = int(spawn_cy - (br.y + br.h // 2))
        elif w is not None and h is not None and w > 0 and h > 0:
            rel_x = int(spawn_cx - (w // 2))
            rel_y = int(spawn_cy - (h // 2))
    except Exception:
        pass
    scale = (int(w), int(h)) if (w is not None and h is not None and w > 0 and h > 0) else None
    return rel_x, rel_y, scale


def append_building_object_in_world(world, inst_entry: dict, tpl_entry: Optional[dict], img_path: Optional[str]) -> None:
    try:
        rel_x = int(inst_entry.get('rel_x') or 0)
        rel_y = int(inst_entry.get('rel_y') or 0)
        image_path = img_path or ''
        solid = True
        split_ratio = 0.5
        z_bottom = None
        z_top = None
        scale = None
        if isinstance(tpl_entry, dict):
            solid = bool(tpl_entry.get('solid', True))
            try:
                split_ratio = float(tpl_entry.get('split_ratio', 0.5))
            except Exception:
                split_ratio = 0.5
            z_bottom = tpl_entry.get('z_bottom')
            z_top = tpl_entry.get('z_top')
        try:
            if isinstance(inst_entry.get('overrides'), dict) and isinstance(inst_entry['overrides'].get('scale'), (list, tuple)):
                sc = inst_entry['overrides']['scale']
                if len(sc) >= 2:
                    scale = (int(sc[0]), int(sc[1]))
        except Exception:
            scale = None
        try:
            if isinstance(inst_entry.get('overrides'), dict) and (inst_entry['overrides'].get('split_ratio') is not None):
                try:
                    sr = float(inst_entry['overrides']['split_ratio'])
                    split_ratio = max(0.05, min(sr, 0.95))
                except Exception:
                    pass
        except Exception:
            pass

        b = Building(
            rel_x=rel_x,
            rel_y=rel_y,
            image_path=image_path,
            solid=solid,
            scale=scale,
            split_ratio=split_ratio,
            z_bottom=z_bottom,
            z_top=z_top,
        )
        try:
            setattr(b, 'id', inst_entry.get('id'))
        except Exception:
            pass
        try:
            setattr(b, 'zone', inst_entry.get('zone'))
        except Exception:
            pass
        try:
            setattr(b, '_is_spawner_visual', True)
            sid = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id')
            if sid is not None:
                setattr(b, 'spawner_instance_id', sid)
                setattr(b, 'spawn_id', sid)
        except Exception:
            pass
        try:
            sid_val = inst_entry.get('spawner_instance_id') or (inst_entry.get('overrides') or {}).get('spawner_instance_id') or inst_entry.get('spawn_id')
            bid_val = inst_entry.get('id')
            if sid_val is not None and bid_val is not None:
                try:
                    sid_str = str(sid_val)
                    bid_int = int(bid_val)
                except Exception:
                    sid_str = None
                    bid_int = None
                if sid_str is not None and bid_int is not None:
                    for inst in (load_instances() or []):
                        try:
                            if str(inst.get('id')) != sid_str:
                                continue
                            vis = inst.get('visuals') if isinstance(inst.get('visuals'), dict) else {}
                            for _, v in list(vis.items()):
                                try:
                                    if isinstance(v, dict):
                                        vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                                    else:
                                        vid = int(v)
                                except Exception:
                                    vid = None
                                if vid is not None and int(vid) == int(bid_int):
                                    try:
                                        if isinstance(v, dict) and (v.get('split_ratio') is not None):
                                            sr = float(v.get('split_ratio'))
                                            sr = max(0.05, min(sr, 0.95))
                                            b.split_ratio = float(sr)
                                    except Exception:
                                        pass
                                    raise StopIteration
                            raise StopIteration
                        except StopIteration:
                            break
                        except Exception:
                            continue
        except Exception:
            pass
        try:
            if getattr(world, 'buildings', None) is None:
                world.buildings = []
        except Exception:
            pass
        try:
            for ob in getattr(world, 'buildings', []) or []:
                if getattr(ob, 'id', None) == inst_entry.get('id'):
                    return
            world.buildings.append(b)
        except Exception:
            pass
    except Exception:
        pass


def persist_spawner_instance_visuals(inst_id: Optional[str], visuals: dict, ensure_visible_in_game: bool = True) -> None:
    if not inst_id:
        return
    base = config.DATA_DIR
    path = os.path.join(base, "spawners", "spawners_instances.json")
    try:
        with open(path, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return
    except FileNotFoundError:
        return
    except Exception:
        return
    changed = False
    for i, e in enumerate(data):
        try:
            if str(e.get('id')) == str(inst_id):
                if e.get('visuals') != visuals:
                    e['visuals'] = visuals
                    changed = True
                if ensure_visible_in_game:
                    ov = dict(e.get('overrides') or {})
                    if not bool(ov.get('visible_in_game', False)):
                        ov['visible_in_game'] = True
                        e['overrides'] = ov
                        changed = True
                break
        except Exception:
            continue
    if changed:
        try:
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(data, f, ensure_ascii=False, indent=4)
        except Exception:
            pass


def auto_repair_state_visuals(world, eid: int, cfg, inst: dict) -> None:
    """Ensure visuals mapping produces valid Building instances in disk and memory.

    - Creates missing building instances from template_id.
    - Updates cfg.state_visuals and instance visuals mapping.
    - Persists to spawners_instances.json when needed.
    """
    vis = inst.get('visuals') if isinstance(inst, dict) else None
    if not isinstance(vis, dict) or not vis:
        return
    b_arr = load_buildings_instances_json()
    existing_ids = set()
    max_id = 0
    for e in b_arr:
        try:
            eid_ = int(e.get('id'))
            existing_ids.add(eid_)
            if eid_ > max_id:
                max_id = eid_
        except Exception:
            continue
    templates = load_buildings_templates_json()
    tmap = {}
    for t in templates:
        try:
            tmap[int(t.get('id'))] = t
        except Exception:
            continue
    try:
        zone = str(inst.get('zone')) if inst.get('zone') is not None else 'lobby'
    except Exception:
        zone = 'lobby'
    try:
        local_tile = inst.get('tile') or (0, 0)
        local_tile = (int(local_tile[0]), int(local_tile[1]))
    except Exception:
        local_tile = (0, 0)

    updated_visuals = False
    if getattr(cfg, 'state_visuals', None) is None:
        try:
            cfg.state_visuals = {}
        except Exception:
            pass

    for key, val in list(vis.items()):
        cur_iid = None
        tpl_id = None
        visuals_scale: Optional[Tuple[int, int]] = None
        if isinstance(val, dict):
            try:
                cur_iid = int(val.get('instance_id') or val.get('id') or val.get('building_instance_id'))
            except Exception:
                cur_iid = None
            try:
                tpl_id = int(val.get('template_id')) if val.get('template_id') is not None else None
            except Exception:
                tpl_id = None
            try:
                off = val.get('offset')
                if isinstance(off, (list, tuple)) and len(off) == 2:
                    dx, dy = int(off[0]), int(off[1])
                    try:
                        if getattr(cfg, 'visuals_offsets_px', None) is None:
                            cfg.visuals_offsets_px = {}
                    except Exception:
                        pass
                    try:
                        cfg.visuals_offsets_px[str(key).strip().lower()] = (dx, dy)
                    except Exception:
                        pass
                sc = val.get('scale')
                if isinstance(sc, (list, tuple)) and len(sc) == 2:
                    try:
                        sw, sh = int(sc[0]), int(sc[1])
                        if sw > 0 and sh > 0:
                            visuals_scale = (sw, sh)
                    except Exception:
                        visuals_scale = None
            except Exception:
                pass
        else:
            try:
                cur_iid = int(val)
            except Exception:
                cur_iid = None

        if cur_iid is not None and cur_iid in existing_ids:
            try:
                cfg.state_visuals[str(key)] = int(cur_iid)
            except Exception:
                pass
            if visuals_scale is not None:
                try:
                    changed_bi = False
                    for e in b_arr:
                        try:
                            if int(e.get('id')) != int(cur_iid):
                                continue
                        except Exception:
                            continue
                        ov = e.get('overrides') or {}
                        if not isinstance(ov, dict):
                            ov = {}
                        try:
                            cur_sc = ov.get('scale')
                            cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
                        except Exception:
                            cur_sc_t = None
                        if cur_sc_t != visuals_scale:
                            ov['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]
                            e['overrides'] = ov
                            changed_bi = True
                        break
                    if changed_bi:
                        try:
                            write_buildings_instances_json(b_arr)
                        except Exception:
                            logger.warning("[SpawnerPlacementSystem] Could not persist scale override for existing building instance")
                except Exception:
                    pass
            continue

        if tpl_id is None or tpl_id not in tmap:
            continue
        tpl_entry = tmap.get(tpl_id)
        img_path = get_template_image_path(templates, tpl_id)
        rel_x, rel_y, scale = calc_centered_rel(local_tile, tpl_entry, img_path)
        new_id = max_id + 1
        max_id = new_id
        entry = {
            'id': int(new_id),
            'template_id': int(tpl_id),
            'zone': zone,
            'rel_x': int(rel_x),
            'rel_y': int(rel_y),
            'overrides': {
                '_is_spawner_visual': True,
            },
            'spawn_id': str(inst.get('id')) if inst.get('id') is not None else None,
            'spawner_instance_id': str(inst.get('id')) if inst.get('id') is not None else None,
        }
        if visuals_scale is not None:
            try:
                entry['overrides']['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]  # type: ignore[index]
            except Exception:
                pass
        elif scale is not None:
            try:
                entry['overrides']['scale'] = [int(scale[0]), int(scale[1])]  # type: ignore[index]
            except Exception:
                pass
        try:
            if inst.get('id') is not None:
                entry['overrides']['spawner_instance_id'] = str(inst.get('id'))
        except Exception:
            pass
        b_arr.append(entry)
        try:
            write_buildings_instances_json(b_arr)
            existing_ids.add(int(new_id))
        except Exception:
            logger.warning("[SpawnerPlacementSystem] Could not persist buildings_instances for auto-repair")
        append_building_object_in_world(world, entry, tpl_entry, img_path)
        try:
            cfg.state_visuals[str(key)] = int(new_id)
        except Exception:
            pass
        try:
            preserved_offset = None
            try:
                if isinstance(val, dict) and isinstance(val.get('offset'), (list, tuple)) and len(val.get('offset')) == 2:
                    preserved_offset = [int(val['offset'][0]), int(val['offset'][1])]
            except Exception:
                preserved_offset = None
            if isinstance(val, dict):
                entry_map = dict(val)
            else:
                entry_map = {}
            entry_map['instance_id'] = int(new_id)
            entry_map['template_id'] = int(tpl_id)
            if preserved_offset is not None:
                entry_map['offset'] = preserved_offset  # type: ignore[index]
            vis[str(key)] = entry_map
            updated_visuals = True
        except Exception:
            pass
        try:
            if not getattr(cfg, 'visible_in_game', False):
                cfg.visible_in_game = True
        except Exception:
            pass

    if updated_visuals:
        try:
            persist_spawner_instance_visuals(str(inst.get('id')) if inst.get('id') is not None else None, vis, ensure_visible_in_game=True)
        except Exception:
            pass


def preflight_validate_spawner_visuals() -> int:
    """Batch-validate and repair spawner visuals across all instances on disk.

    - Ensures that every visuals[*] mapping points to an existing building instance id.
    - Creates missing building instances from template_id with `_is_spawner_visual: true`.
    - Persists updated visuals maps back to spawners_instances.json.

    Returns the number of spawner instances updated.
    """
    try:
        # Load data sources
        instances = load_instances() or []
        b_arr = load_buildings_instances_json()
        templates = load_buildings_templates_json()
        tmap: Dict[int, dict] = {}
        for t in templates:
            try:
                tmap[int(t.get('id'))] = t
            except Exception:
                continue
        existing_ids = set()
        max_id = 0
        for e in b_arr:
            try:
                bid = int(e.get('id'))
                existing_ids.add(bid)
                if bid > max_id:
                    max_id = bid
            except Exception:
                continue

        total_updated = 0
        for inst in instances:
            try:
                vis = inst.get('visuals') if isinstance(inst, dict) else None
                if not isinstance(vis, dict) or not vis:
                    continue
                zone = str(inst.get('zone')) if inst.get('zone') is not None else 'lobby'
                try:
                    local_tile = inst.get('tile') or (0, 0)
                    local_tile = (int(local_tile[0]), int(local_tile[1]))
                except Exception:
                    local_tile = (0, 0)
                inst_updated = False
                for key, val in list(vis.items()):
                    cur_iid = None
                    tpl_id = None
                    visuals_scale: Optional[Tuple[int, int]] = None
                    if isinstance(val, dict):
                        try:
                            cur_iid = int(val.get('instance_id') or val.get('id') or val.get('building_instance_id'))
                        except Exception:
                            cur_iid = None
                        try:
                            tpl_id = int(val.get('template_id')) if val.get('template_id') is not None else None
                        except Exception:
                            tpl_id = None
                        sc = val.get('scale') if isinstance(val, dict) else None
                        if isinstance(sc, (list, tuple)) and len(sc) == 2:
                            try:
                                sw, sh = int(sc[0]), int(sc[1])
                                if sw > 0 and sh > 0:
                                    visuals_scale = (sw, sh)
                            except Exception:
                                visuals_scale = None
                    else:
                        try:
                            cur_iid = int(val)
                        except Exception:
                            cur_iid = None

                    # If we already have a valid building instance id, enforce schema tags and optional scale
                    if cur_iid is not None and cur_iid in existing_ids:
                        try:
                            changed_bi = False
                            for e in b_arr:
                                try:
                                    if int(e.get('id')) != int(cur_iid):
                                        continue
                                except Exception:
                                    continue
                                if not bool(e.get('spawner_visual', False)):
                                    e['spawner_visual'] = True
                                    changed_bi = True
                                ov = e.get('overrides') or {}
                                if not isinstance(ov, dict):
                                    ov = {}
                                if not bool(ov.get('_is_spawner_visual', False)):
                                    ov['_is_spawner_visual'] = True
                                    changed_bi = True
                                sid = str(inst.get('id')) if inst.get('id') is not None else None
                                if sid:
                                    if str(e.get('spawn_id') or '') != sid:
                                        e['spawn_id'] = sid
                                        changed_bi = True
                                    if str(e.get('spawner_instance_id') or '') != sid:
                                        e['spawner_instance_id'] = sid
                                        changed_bi = True
                                    if str((ov or {}).get('spawner_instance_id') or '') != sid:
                                        ov['spawner_instance_id'] = sid
                                        changed_bi = True
                                # Enforce scale when visuals include scale
                                if visuals_scale is not None:
                                    try:
                                        cur_sc = ov.get('scale')
                                        cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
                                    except Exception:
                                        cur_sc_t = None
                                    if cur_sc_t != visuals_scale:
                                        ov['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]
                                        changed_bi = True
                                e['overrides'] = ov
                                break
                            if changed_bi:
                                write_buildings_instances_json(b_arr)
                        except Exception:
                            pass
                        continue

                    # Need template to create a new instance
                    if tpl_id is None or tpl_id not in tmap:
                        continue
                    tpl_entry = tmap.get(tpl_id)
                    img_path = get_template_image_path(templates, tpl_id)
                    rel_x, rel_y, scale = calc_centered_rel(local_tile, tpl_entry, img_path)
                    new_id = max_id + 1
                    max_id = new_id
                    entry = {
                        'id': int(new_id),
                        'template_id': int(tpl_id),
                        'zone': zone,
                        'rel_x': int(rel_x),
                        'rel_y': int(rel_y),
                        'spawner_visual': True,
                        'overrides': {
                            '_is_spawner_visual': True,
                        },
                        'spawn_id': str(inst.get('id')) if inst.get('id') is not None else None,
                        'spawner_instance_id': str(inst.get('id')) if inst.get('id') is not None else None,
                    }
                    if visuals_scale is not None:
                        try:
                            entry['overrides']['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]  # type: ignore[index]
                        except Exception:
                            pass
                    elif scale is not None:
                        try:
                            entry['overrides']['scale'] = [int(scale[0]), int(scale[1])]  # type: ignore[index]
                        except Exception:
                            pass
                    try:
                        if inst.get('id') is not None:
                            entry['overrides']['spawner_instance_id'] = str(inst.get('id'))
                    except Exception:
                        pass
                    b_arr.append(entry)
                    try:
                        write_buildings_instances_json(b_arr)
                        existing_ids.add(int(new_id))
                    except Exception:
                        logger.warning("[SpawnerPlacementSystem][preflight] Could not persist buildings_instances for auto-repair")
                    # Update visuals mapping on the spawner instance
                    try:
                        preserved_offset = None
                        try:
                            if isinstance(val, dict) and isinstance(val.get('offset'), (list, tuple)) and len(val.get('offset')) == 2:
                                preserved_offset = [int(val['offset'][0]), int(val['offset'][1])]
                        except Exception:
                            preserved_offset = None
                        entry_map = dict(val) if isinstance(val, dict) else {}
                        entry_map['instance_id'] = int(new_id)
                        entry_map['template_id'] = int(tpl_id)
                        if preserved_offset is not None:
                            entry_map['offset'] = preserved_offset  # type: ignore[index]
                        vis[str(key)] = entry_map
                        inst_updated = True
                    except Exception:
                        pass
                if inst_updated:
                    try:
                        persist_spawner_instance_visuals(str(inst.get('id')) if inst.get('id') is not None else None, vis, ensure_visible_in_game=True)
                        total_updated += 1
                    except Exception:
                        pass
            except Exception:
                # best-effort: continue with next instance
                continue
        try:
            if total_updated:
                logger.info("[SpawnerPlacementSystem][preflight] Updated %s spawner visuals", total_updated)
        except Exception:
            pass
        return total_updated
    except Exception:
        logger.exception("[SpawnerPlacementSystem][preflight] Failed preflight spawner visuals", exc_info=False)
        return 0
