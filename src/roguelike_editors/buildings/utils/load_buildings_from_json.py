import os
import json
from typing import List
from roguelike_engine.config.config import (
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
    BUILDINGS_TEMPLATES_PATH,
    BUILDINGS_INSTANCES_PATH,
)
from roguelike_engine.z_layer.persistence import extract_z_from_json
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings

from roguelike_engine.buildings.building import Building

import logging
logger = logging.getLogger(__name__)

def _normalize_asset_path(p):
    try:
        if not p or not isinstance(p, str):
            return p
        q = p.replace("\\", "/")
        while '//' in q:
            q = q.replace('//', '/')
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return p

def _canonicalize_zone(zone: str) -> str:
    """
    Map arbitrary zone label from JSON to the canonical key used in
    global_map_settings.zone_offsets. Performs case-insensitive match and
    normalizes base zones ('lobby', 'dungeon') to lowercase.
    """
    try:
        if not zone or not isinstance(zone, str):
            return zone
        # Respect sentinel value used when an entity is intentionally outside any zone
        if zone.lower() == "no zone":
            return "no zone"
        offsets = getattr(global_map_settings, 'zone_offsets', {}) or {}
        # Exact match first
        if zone in offsets:
            return zone
        low = zone.lower()
        # Normalize known base zones
        if low in ("lobby", "dungeon") and low in offsets:
            return low
        # Case-insensitive lookup among existing keys
        for k in offsets.keys():
            if k.lower() == low:
                return k
        # Fallback: return original and warn
        logger.warning(f"[Buildings] Zone '{zone}' not found in offsets (keys={list(offsets.keys())}). Using as-is; building may be misaligned.")
        return zone
    except Exception:
        return zone

def _load_collisions_sources():
    """Load collisions exclusively from split files. No legacy fallback.

    Split files format (each file is a plain dict):
      - BUILDINGS_COLLISIONS_BY_IMAGE_PATH -> { image_path: {width,height,collision} }
      - BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH -> { spawn_id: {width,height,collision} }
      - BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH -> { id: {width,height,collision} }

    If none of the split files exist, returns empty dicts and logs a warning.
    """
    def _read_dict(path):
        try:
            if os.path.exists(path):
                with open(path, 'r', encoding='utf-8-sig') as f:
                    d = json.load(f) or {}
                    return d if isinstance(d, dict) else {}
        except Exception:
            return {}
        return {}

    exists_any = any(
        os.path.exists(p) for p in (
            BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
            BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
            BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
        )
    )
    if not exists_any:
        logger.warning("[Buildings] Split collisions files not found; using empty collisions maps.")
        return {}, {}, {}

    collisions_global = _read_dict(BUILDINGS_COLLISIONS_BY_IMAGE_PATH)
    collisions_instances = _read_dict(BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH)
    collisions_by_id = _read_dict(BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH)
    return collisions_global, collisions_instances, collisions_by_id

def _apply_collision_for_building(b: Building,
                                  entry: dict,
                                  collisions_global: dict,
                                  collisions_instances: dict,
                                  collisions_by_id: dict):
    """Initialize collision_map respecting collider_scope.

    - If scope == 'CU': prefer by_building_instance_id -> legacy by_spawn_id -> by_image_path
    - If scope == 'CG' (default): use by_image_path only (ignore per-instance overrides)

    Also applies additional inline per-instance override if collider_scope == 'CU'."""
    from roguelike_engine.config.config_tiles import TILE_SIZE as _TS
    _img_path = _normalize_asset_path((entry.get("assets") or {}).get("idle"))
    # Select base collision entry (depends on desired scope)
    coll_entry = None
    try:
        scope = entry.get("collider_scope", "CG")
        if scope == 'CU':
            # 1) Per-building-instance collisions (new scheme)
            # Prefer instance id on the Building object; fallback to any id in entry (template id)
            bid = getattr(b, "id", None)
            if bid is None:
                bid = entry.get("id")
            if bid is not None:
                bid_str = str(bid)
                if bid_str in collisions_by_id:
                    coll_entry = collisions_by_id.get(bid_str)
            # 2) Legacy per-spawn override (fallback)
            if not coll_entry:
                sid = getattr(b, "spawn_id", None)
                if sid and sid in collisions_instances:
                    coll_entry = collisions_instances.get(sid)
            # 3) Global by image_path
            if not coll_entry:
                coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or '').replace('/', '\\'))
        else:
            # CG: only by image_path
            coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or '').replace('/', '\\'))
    except Exception:
        coll_entry = collisions_global.get(_img_path) or collisions_global.get((_img_path or '').replace('/', '\\'))

    desired_cols = max(1, (b.image.get_width() + _TS - 1) // _TS)
    desired_rows = max(1, (b.image.get_height() + _TS - 1) // _TS)
    if coll_entry and "collision" in coll_entry:
        src = [row[:] for row in coll_entry["collision"]]
        cur_rows = len(src)
        cur_cols = len(src[0]) if cur_rows > 0 else 0
        # Normalize rows
        if cur_rows < desired_rows:
            for _ in range(desired_rows - cur_rows):
                src.append(["." for _ in range(cur_cols or desired_cols)])
            cur_rows = desired_rows
        elif cur_rows > desired_rows:
            src = src[:desired_rows]
            cur_rows = desired_rows
        # Normalize cols
        if cur_cols < desired_cols:
            for r in range(cur_rows):
                if cur_cols == 0:
                    src[r] = ["."] * desired_cols
                else:
                    src[r].extend(["."] * (desired_cols - cur_cols))
        elif cur_cols > desired_cols:
            for r in range(cur_rows):
                src[r] = src[r][:desired_cols]
        b.collision_map = src
    else:
        # default empty map sized to image ceil
        w = desired_cols
        h = desired_rows
        b.collision_map = [["." for _ in range(w)] for _ in range(h)]

    # If collider scope is CU and instance override present, apply on top
    try:
        if entry.get("collider_scope", "CG") == "CU":
            ov = entry.get("collision_override")
            if ov and "collision" in ov:
                src = [row[:] for row in ov["collision"]]
                cur_rows = len(src)
                cur_cols = len(src[0]) if cur_rows > 0 else 0
                if cur_rows < desired_rows:
                    for _ in range(desired_rows - cur_rows):
                        src.append(["." for _ in range(cur_cols or desired_cols)])
                    cur_rows = desired_rows
                elif cur_rows > desired_rows:
                    src = src[:desired_rows]
                    cur_rows = desired_rows
                if cur_cols < desired_cols:
                    for r in range(cur_rows):
                        if cur_cols == 0:
                            src[r] = ["."] * desired_cols
                        else:
                            src[r].extend(["."] * (desired_cols - cur_cols))
                elif cur_cols > desired_cols:
                    for r in range(cur_rows):
                        src[r] = src[r][:desired_cols]
                b.collision_map = src
    except Exception:
        pass

def _load_from_split(z_state=None) -> List[Building]:
    """Load buildings by merging templates and instances JSON files."""
    # Load collisions sources once
    collisions_global, collisions_instances, collisions_by_id = _load_collisions_sources()

    # Load templates
    try:
        with open(BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8-sig') as tf:
            templates_raw = json.load(tf) or []
    except FileNotFoundError:
        logger.warning(f"[Buildings] Templates file not found: {BUILDINGS_TEMPLATES_PATH}")
        templates_raw = []
    except Exception as e:
        logger.error(f"[Buildings] Error reading templates: {e}")
        templates_raw = []
    # Build map id->template dict
    tmap = {}
    for t in templates_raw:
        if not isinstance(t, dict):
            continue
        tid = t.get('id')
        if tid is None:
            # Try fallback to stringified idle image path as id
            try:
                idle = (t.get('assets') or {}).get('idle')
                if idle:
                    tid = _normalize_asset_path(idle)
            except Exception:
                pass
        if tid is None:
            continue
        tmap[str(tid)] = dict(t)

    # Load instances
    try:
        with open(BUILDINGS_INSTANCES_PATH, 'r', encoding='utf-8-sig') as inf:
            instances_raw = json.load(inf) or []
        if not isinstance(instances_raw, list):
            instances_raw = []
    except FileNotFoundError:
        instances_raw = []
    except Exception as e:
        logger.error(f"[Buildings] Error reading instances: {e}")
        instances_raw = []

    buildings: List[Building] = []
    for inst in instances_raw:
        try:
            if not isinstance(inst, dict):
                continue
            tpl_id = inst.get('template_id')
            if tpl_id is None:
                logger.warning(f"[Buildings] Instance without template_id: {inst}")
                continue
            tpl = tmap.get(str(tpl_id))
            if not tpl:
                logger.warning(f"[Buildings] Missing template id={tpl_id} for instance {inst}")
                continue

            # Merge template with overrides
            entry = dict(tpl)
            overrides = inst.get('overrides')
            if isinstance(overrides, dict):
                try:
                    # shallow merge, overrides take precedence
                    entry.update(overrides)
                except Exception:
                    pass

            # Position/zone from instance
            # Prefer pixel rel_x/rel_y; fallback to tile -> pixels
            rel_x = inst.get('rel_x')
            rel_y = inst.get('rel_y')
            if rel_x is None or rel_y is None:
                try:
                    tile = inst.get('tile') or inst.get('local_tile')
                    if tile is not None:
                        tx, ty = int(tile[0]), int(tile[1])
                        rel_x, rel_y = tx * TILE_SIZE, ty * TILE_SIZE
                except Exception:
                    pass
            rel_x = int(rel_x or 0)
            rel_y = int(rel_y or 0)
            entry['rel_x'] = rel_x
            entry['rel_y'] = rel_y
            if inst.get('zone'):
                entry['zone'] = _canonicalize_zone(inst['zone'])

            # Bind instance id into merged entry to support per-instance lookups
            try:
                if inst.get('id') is not None:
                    entry['id'] = inst.get('id')
            except Exception:
                pass

            # Ensure assets.idle exists after merge
            assets = entry.get('assets') or {}
            img_idle = _normalize_asset_path(assets.get('idle')) if isinstance(assets, dict) else None
            if not img_idle:
                logger.warning(f"[Buildings] Skipping instance without assets.idle after merge (tpl={tpl_id})")
                continue

            b = Building(
                rel_x=entry.get("rel_x", 0),
                rel_y=entry.get("rel_y", 0),
                image_path=img_idle,
                solid=entry.get("solid", True),
                scale=tuple(entry["scale"]) if "scale" in entry else None,
                split_ratio=entry.get("split_ratio", 0.5),
                z_bottom=entry.get("z_bottom"),
                z_top=entry.get("z_top"),
            )

            # Bind identifiers on object for downstream systems
            try:
                if inst.get('id') is not None:
                    setattr(b, 'id', inst.get('id'))
            except Exception:
                pass
            try:
                # Maintain spawn_id semantics if provided by instance
                sid = inst.get('spawn_id') or inst.get('spawner_instance_id')
                if sid is not None:
                    setattr(b, 'spawn_id', str(sid))
                    setattr(b, 'spawner_instance_id', str(sid))
            except Exception:
                pass

            # Collision map selection and overrides
            _apply_collision_for_building(b, entry, collisions_global, collisions_instances, collisions_by_id)

            # Apply Z-layer from merged entry
            if z_state:
                extract_z_from_json(entry, z_state, b)

            # Zone assignment
            if entry.get('zone'):
                b.zone = _canonicalize_zone(entry['zone'])

            # Multi-image visual mapping
            try:
                images_by_state = entry.get("images_by_state")
                if isinstance(images_by_state, dict) and images_by_state:
                    initial_state = entry.get("initial_visual_state")
                    b.model.set_images_by_state(images_by_state, initial_state=initial_state)
                thresholds = entry.get("state_thresholds")
                if thresholds is not None:
                    b.model.set_state_thresholds(thresholds if isinstance(thresholds, list) else None)
            except Exception as _e:
                logger.warning(f"[Buildings][loader/split] Could not apply images_by_state/state_thresholds: {_e}", exc_info=False)

            # Collider scope
            try:
                b.collider_scope = entry.get("collider_scope", "CG")
            except Exception:
                pass

            # Restore original scale if provided
            if entry.get("original_scale"):
                b.original_scale = tuple(entry["original_scale"])

            buildings.append(b)
        except Exception as e:
            logger.error(f"[Buildings][split] Error creating building from instance: {e}")

    logger.info(f"[Buildings][Cargando Edificios SPLIT] {len(buildings)} edificios (templates+instances)")
    return buildings

def load_buildings_from_json(
    z_state=None
) -> List:
    """
    Carga edificios desde JSON en modo split (templates + instances) usando coordenadas relativas.
    - Si `z_state` se proporciona, inyecta la capa Z en los objetos creados.
    - Si faltan los archivos split requeridos, devuelve lista vacía con warning (no hay fallback legacy).
    """
    if os.path.exists(BUILDINGS_TEMPLATES_PATH) and os.path.exists(BUILDINGS_INSTANCES_PATH):
        return _load_from_split(z_state)
    logger.warning(
        f"[Buildings][split] Archivos requeridos no encontrados: templates={BUILDINGS_TEMPLATES_PATH} instances={BUILDINGS_INSTANCES_PATH}. No se cargan edificios."
    )
    return []