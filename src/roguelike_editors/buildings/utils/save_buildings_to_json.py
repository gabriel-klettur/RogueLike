import os
import json
from typing import Dict, Tuple, Optional
from roguelike_engine.config.config import (
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
    zone_offsets: Optional[Dict] = None,
    **kwargs
):
    """
    [DEPRECADO] Guardado legacy.
    Esta función se mantiene por compatibilidad, pero delega SIEMPRE a save_buildings_split.
    Ignora `filepath` y persiste en:
      - BUILDINGS_TEMPLATES_PATH
      - BUILDINGS_INSTANCES_PATH
    """
    logger.warning("[Buildings][Deprecated] save_buildings_to_json() delega a save_buildings_split(); usa el modo split")
    return save_buildings_split(
        buildings,
        z_state=z_state,
        zone_offsets=zone_offsets,
    )


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

    skipped_spawner_visuals = 0
    for b in buildings:
        try:
            # Do not persist spawner visuals (runtime/link-only)
            try:
                if getattr(b, '_is_spawner_visual', False) or getattr(b, '_spawner_eid', None) is not None:
                    skipped_spawner_visuals += 1
                    continue
            except Exception:
                pass
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

    # Deduplicate instances_out by position/template
    try:
        before = len(instances_out)
        seen: dict[str, dict] = {}
        def _key(e: dict) -> str:
            try:
                return f"{e.get('zone')}|{int(e.get('rel_x') or 0)}|{int(e.get('rel_y') or 0)}|{int(e.get('template_id') or -1)}"
            except Exception:
                return str(id(e))
        def _score(e: dict) -> tuple:
            has_sid = 1 if (e.get('spawn_id') is not None) else 0
            try:
                neg_id = -int(e.get('id') or 0)
            except Exception:
                neg_id = 0
            return (has_sid, neg_id)
        for e in instances_out:
            k = _key(e)
            cur = seen.get(k)
            if cur is None:
                seen[k] = e
            else:
                if _score(e) > _score(cur):
                    seen[k] = e
        instances_out = list(seen.values())
        after = len(instances_out)
        if skipped_spawner_visuals:
            logger.info(f"[Buildings][SaveSplit] Skipped spawner visuals: {skipped_spawner_visuals}")
        if after != before:
            logger.debug(f"[Buildings][SaveSplit] Dedup instances by pos/tpl: {before}->{after} (removed={before-after})")
    except Exception:
        pass

    # Write instances (ordered by id for stability)
    try:
        instances_out.sort(key=lambda x: int(x.get('id') or 0))
    except Exception:
        pass
    with open(i_path, 'w', encoding='utf-8') as inf:
        json.dump(instances_out, inf, indent=4)

    logger.info(f"✅ {len(templates_list)} templates guardados en {t_path}")
    logger.info(f"✅ {len(instances_out)} instancias guardadas en {i_path}")