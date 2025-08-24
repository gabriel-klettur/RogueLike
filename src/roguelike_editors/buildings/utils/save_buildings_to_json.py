import os
import json
from typing import Dict, Tuple, Optional
from roguelike_engine.config.config import (
    BUILDINGS_DATA_PATH,
    BUILDINGS_TEMPLATES_PATH,
    BUILDINGS_INSTANCES_PATH,
)
from roguelike_engine.z_layer.persistence import inject_z_into_json
import logging
logger = logging.getLogger(__name__)

from roguelike_engine.config.map_config import global_map_settings

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
    Ensure the zone label persisted to JSON matches the canonical key used by
    global_map_settings.zone_offsets (case-insensitive; 'lobby'/'dungeon' -> lowercase).
    """
    try:
        if not zone or not isinstance(zone, str):
            return zone
        # Respect sentinel value used when a building is intentionally outside any zone
        if zone.lower() == "no zone":
            return "no zone"
        offsets = getattr(global_map_settings, 'zone_offsets', {}) or {}
        if zone in offsets:
            return zone
        low = zone.lower()
        if low in ("lobby", "dungeon") and low in offsets:
            return low
        for k in offsets.keys():
            if k.lower() == low:
                return k
        # If still not found, keep original but warn
        logger.warning(f"[Buildings][Save] Zone '{zone}' not found in offsets; saving as-is.")
        return zone
    except Exception:
        return zone

def save_buildings_to_json(
    buildings,
    filepath: Optional[str] = None,
    z_state=None,
    zone_offsets: Optional[Dict[str, Tuple[int, int]]] = None,
    **kwargs
):
    """
    Guarda la lista de buildings en un JSON usando coordenadas relativas.
    - Si `filepath` es proporcionado, se usa esa ruta; si no, BUILDINGS_DATA_PATH.
    - Si se proporciona `z_state`, inyecta la capa Z de cada edificio.
    """
    target = filepath or BUILDINGS_DATA_PATH
    data = []
    seen_spawn_ids = set()  # Deduplicate by spawn_id for spawner-linked buildings

    # Preparar asignación de IDs únicos (auto-incremental y persistente)
    used_ids = set()
    # IDs existentes en memoria
    for b0 in buildings:
        try:
            bid = getattr(b0, 'id', None)
            if bid is not None and str(bid).isdigit():
                used_ids.add(int(bid))
        except Exception:
            pass
    # IDs existentes en disco (para evitar colisiones)
    try:
        if os.path.exists(target):
            with open(target, 'r', encoding='utf-8') as rf:
                prev = json.load(rf) or []
            if isinstance(prev, list):
                for e in prev:
                    try:
                        pid = e.get('id')
                        if pid is not None and str(pid).isdigit():
                            used_ids.add(int(pid))
                    except Exception:
                        pass
    except Exception:
        pass
    next_id = (max(used_ids) + 1) if used_ids else 1

    for b in buildings:
        try:
            zone_norm = _canonicalize_zone(b.zone)
            relx = int(b.rel_x)
            rely = int(b.rel_y)
            img = _normalize_asset_path(b.image_path)
            spawn_id = getattr(b, 'spawn_id', None) or getattr(b, 'spawner_instance_id', None)

            if spawn_id:
                sid = str(spawn_id)
                if sid in seen_spawn_ids:
                    try:
                        logger.debug(f"[Buildings][Save] Skipping duplicate spawn_id={sid}")
                    except Exception:
                        pass
                    continue
                seen_spawn_ids.add(sid)

            building_data = {
                "zone": zone_norm,
                "rel_x": relx,
                "rel_y": rely,
                "assets": {"idle": img},
                "solid": b.solid,
                "scale": [b.image.get_width(), b.image.get_height()],
                "original_scale": list(b.original_scale) if getattr(b, "original_scale", None) else None,
                "split_ratio": round(b.split_ratio, 3),
                "z_bottom": b.z_bottom,
                "z_top": b.z_top,
                "collider_scope": getattr(b, "collider_scope", "CG"),
            }

            # ID del edificio (auto-asignado si falta)
            try:
                bid = getattr(b, 'id', None)
            except Exception:
                bid = None
            if bid is None or not str(bid).isdigit():
                bid = next_id
                next_id += 1
                try:
                    setattr(b, 'id', bid)
                except Exception:
                    pass
            building_data["id"] = int(bid)

            if spawn_id:
                building_data["spawn_id"] = str(spawn_id)

            if z_state:
                building_data["z"] = inject_z_into_json(b, z_state)

            # Persistir override de colisiones por instancia si el alcance es CU
            if building_data.get("collider_scope") == "CU" and getattr(b, "collision_map", None):
                rows = len(b.collision_map)
                cols = len(b.collision_map[0]) if rows > 0 else 0
                building_data["collision_override"] = {
                    "width": cols,
                    "height": rows,
                    "collision": b.collision_map,
                }

            data.append(building_data)

        except Exception as e:
            logger.error(f"⚠️ Error al procesar un edificio: {e}")

    if not data:
        logger.warning("⚠️ No se encontraron edificios válidos para guardar.")
        return

    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=4)

    logger.info(f"✅ {len(data)} edificios guardados en {target}")


# ──────────────────────────────────────────────────────────────────────────────
# Split persistence: templates.json + instances.json
# ──────────────────────────────────────────────────────────────────────────────

def _template_signature_from_entry(e: dict) -> str:
    try:
        img = _normalize_asset_path(((e.get('assets') or {}).get('idle')) if isinstance(e.get('assets'), dict) else None)
        solid = bool(e.get('solid', True))
        split_ratio = round(float(e.get('split_ratio', 0.5)), 3)
        collider_scope = e.get('collider_scope', 'CG')
        original_scale = e.get('original_scale') if isinstance(e.get('original_scale'), (list, tuple)) else None
        sig = {
            'img': img,
            'solid': solid,
            'split_ratio': split_ratio,
            'collider_scope': collider_scope,
            'original_scale': list(original_scale) if original_scale else None,
        }
        return json.dumps(sig, sort_keys=True, ensure_ascii=False)
    except Exception:
        return json.dumps({'e': 'invalid'}, sort_keys=True)

def _template_signature_from_building(b) -> str:
    try:
        img = _normalize_asset_path(getattr(b, 'image_path', None))
        solid = bool(getattr(b, 'solid', True))
        split_ratio = round(float(getattr(b, 'split_ratio', 0.5)), 3)
        collider_scope = getattr(b, 'collider_scope', 'CG')
        original_scale = getattr(b, 'original_scale', None)
        sig = {
            'img': img,
            'solid': solid,
            'split_ratio': split_ratio,
            'collider_scope': collider_scope,
            'original_scale': list(original_scale) if original_scale else None,
        }
        return json.dumps(sig, sort_keys=True, ensure_ascii=False)
    except Exception:
        return json.dumps({'b': 'invalid'}, sort_keys=True)

def _build_template_entry_from_building(b) -> dict:
    entry = {
        'assets': {'idle': _normalize_asset_path(getattr(b, 'image_path', None))},
        'solid': bool(getattr(b, 'solid', True)),
        'split_ratio': round(float(getattr(b, 'split_ratio', 0.5)), 3),
        'collider_scope': getattr(b, 'collider_scope', 'CG'),
    }
    try:
        if getattr(b, 'original_scale', None):
            entry['original_scale'] = list(getattr(b, 'original_scale'))
    except Exception:
        pass
    return entry

def save_buildings_split(
    buildings,
    z_state=None,
    zone_offsets: Optional[Dict[str, Tuple[int, int]]] = None,
    templates_path: Optional[str] = None,
    instances_path: Optional[str] = None,
):
    """
    Persist buildings into two files:
    - Templates: deduplicated static data with stable IDs.
    - Instances: placement and per-instance overrides referencing templates by template_id.

    Preserves existing IDs where possible and avoids duplicating spawner-linked visuals via spawn_id.
    """
    t_path = templates_path or BUILDINGS_TEMPLATES_PATH
    i_path = instances_path or BUILDINGS_INSTANCES_PATH

    os.makedirs(os.path.dirname(t_path), exist_ok=True)
    os.makedirs(os.path.dirname(i_path), exist_ok=True)

    # Load existing templates to preserve IDs
    existing_templates = []
    try:
        if os.path.exists(t_path):
            with open(t_path, 'r', encoding='utf-8-sig') as tf:
                existing_templates = json.load(tf) or []
            if not isinstance(existing_templates, list):
                existing_templates = []
    except Exception:
        existing_templates = []

    sig_to_tid: dict[str, int] = {}
    tid_to_entry: dict[int, dict] = {}
    max_tid = 0
    # Index existing templates
    for te in existing_templates:
        try:
            tid = int(te.get('id')) if te.get('id') is not None and str(te.get('id')).isdigit() else None
            if tid is None:
                continue
            sig = _template_signature_from_entry(te)
            sig_to_tid[sig] = tid
            tid_to_entry[tid] = te
            if tid > max_tid:
                max_tid = tid
        except Exception:
            continue

    # Load existing instances to preserve instance IDs
    existing_instances = []
    try:
        if os.path.exists(i_path):
            with open(i_path, 'r', encoding='utf-8-sig') as inf:
                existing_instances = json.load(inf) or []
            if not isinstance(existing_instances, list):
                existing_instances = []
    except Exception:
        existing_instances = []

    by_spawn_id: dict[str, int] = {}
    by_pos_key: dict[str, int] = {}
    max_iid = 0
    for inst in existing_instances:
        try:
            iid = int(inst.get('id')) if inst.get('id') is not None and str(inst.get('id')).isdigit() else None
            if iid is None:
                continue
            if inst.get('spawn_id') is not None:
                by_spawn_id[str(inst.get('spawn_id'))] = iid
            k = f"{inst.get('zone')}|{inst.get('rel_x')}|{inst.get('rel_y')}|{inst.get('template_id')}"
            by_pos_key[k] = iid
            if iid > max_iid:
                max_iid = iid
        except Exception:
            continue

    # Build new templates/instances
    seen_spawn_ids = set()
    templates_needed: dict[int, dict] = dict(tid_to_entry)  # start with existing
    instances_out = []

    for b in buildings:
        try:
            # Dedup spawner-linked visuals
            spawn_id = getattr(b, 'spawn_id', None) or getattr(b, 'spawner_instance_id', None)
            if spawn_id:
                sid = str(spawn_id)
                if sid in seen_spawn_ids:
                    continue
                seen_spawn_ids.add(sid)

            # Compute/find template id by signature
            sig = _template_signature_from_building(b)
            tid = sig_to_tid.get(sig)
            if tid is None:
                tid = max_tid + 1
                max_tid = tid
                te = _build_template_entry_from_building(b)
                te['id'] = tid
                templates_needed[tid] = te
                sig_to_tid[sig] = tid

            # Placement
            zone_norm = _canonicalize_zone(getattr(b, 'zone', None))
            relx = int(getattr(b, 'rel_x', 0))
            rely = int(getattr(b, 'rel_y', 0))

            # Instance overrides (per-instance scale, z, collision override when CU)
            overrides = {}
            try:
                w = int(b.image.get_width())
                h = int(b.image.get_height())
                overrides['scale'] = [w, h]
            except Exception:
                pass
            try:
                if getattr(b, 'z_bottom', None) is not None:
                    overrides['z_bottom'] = getattr(b, 'z_bottom')
                if getattr(b, 'z_top', None) is not None:
                    overrides['z_top'] = getattr(b, 'z_top')
            except Exception:
                pass
            try:
                if getattr(b, 'collider_scope', 'CG') == 'CU' and getattr(b, 'collision_map', None):
                    rows = len(b.collision_map)
                    cols = len(b.collision_map[0]) if rows > 0 else 0
                    overrides['collider_scope'] = 'CU'
                    overrides['collision_override'] = {
                        'width': cols,
                        'height': rows,
                        'collision': b.collision_map,
                    }
            except Exception:
                pass
            if not overrides:
                overrides = None

            # Determine instance id (preserve if exists)
            iid = None
            if spawn_id is not None and str(spawn_id) in by_spawn_id:
                iid = by_spawn_id[str(spawn_id)]
            if iid is None:
                pos_key = f"{zone_norm}|{relx}|{rely}|{tid}"
                iid = by_pos_key.get(pos_key)
            if iid is None:
                iid = max_iid + 1
                max_iid = iid

            inst_obj = {
                'id': int(iid),
                'template_id': int(tid),
                'zone': zone_norm,
                'rel_x': relx,
                'rel_y': rely,
            }
            if spawn_id is not None:
                inst_obj['spawn_id'] = str(spawn_id)
            if overrides is not None:
                inst_obj['overrides'] = overrides

            # Persist optional z_state snapshot if requested (for future debugging); we keep in overrides already
            if z_state:
                try:
                    inst_obj.setdefault('overrides', {})
                    inst_obj['overrides']['z'] = inject_z_into_json(b, z_state)
                except Exception:
                    pass

            instances_out.append(inst_obj)
        except Exception as e:
            logger.error(f"[Buildings][SaveSplit] Error procesando edificio: {e}")

    # Write templates (ordered by id)
    templates_list = [templates_needed[k] for k in sorted(templates_needed.keys())]
    with open(t_path, 'w', encoding='utf-8') as tf:
        json.dump(templates_list, tf, indent=4)

    # Write instances
    with open(i_path, 'w', encoding='utf-8') as inf:
        json.dump(instances_out, inf, indent=4)

    logger.info(f"✅ {len(templates_list)} templates guardados en {t_path}")
    logger.info(f"✅ {len(instances_out)} instancias guardadas en {i_path}")