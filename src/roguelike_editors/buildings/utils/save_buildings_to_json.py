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
try:
    import os as _os
    if _os.environ.get('RL_VERBOSE_SAVE') != '1':
        # Demote this module's DEBUG chatter unless explicitly requested
        logger.setLevel(logging.INFO)
except Exception:
    pass

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
    try:
        logger.debug(f"[Buildings][SaveSplit] Existing instances indexed: max_iid={max_iid} by_spawn_id={len(by_spawn_id)} by_pos_key={len(by_pos_key)}")
    except Exception:
        pass

    # Build new templates/instances
    # Aggregated logging counters (to avoid per-instance spam)
    preserved_count = 0
    reused_spawn_count = 0
    reused_pos_count = 0
    new_assigned_count = 0
    _SAMPLE_N = 3
    preserved_samples = []
    reused_spawn_samples = []
    reused_pos_samples = []
    new_assigned_samples = []

    seen_spawn_ids = set()
    templates_needed: dict[int, dict] = dict(tid_to_entry)  # start with existing
    instances_out = []
    # Track IDs assigned during this pass to avoid duplicates
    used_ids: set[int] = set()

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
                    overrides['collider_scope'] = 'CU'
            except Exception:
                pass

            if not overrides:
                overrides = None

            # Determine instance id (preserve if exists)
            iid = None
            # 0) Prefer the in-memory BuildingModel.id if present and not yet used in this pass
            try:
                cur_id_attr = getattr(b, 'id', None)
                cur_id = int(cur_id_attr) if cur_id_attr is not None and str(cur_id_attr).isdigit() else None
            except Exception:
                cur_id = None
            if cur_id is not None:
                if cur_id in used_ids:
                    try:
                        logger.warning(f"[Buildings][SaveSplit] Preserve-id conflict: id={cur_id} already used in this pass; will reassign for building at zone={zone_norm} rel=({relx},{rely}) tpl={tid}")
                    except Exception:
                        pass
                else:
                    iid = cur_id
                    preserved_count += 1
                    if len(preserved_samples) < _SAMPLE_N:
                        preserved_samples.append(iid)

            if iid is None and (spawn_id is not None and str(spawn_id) in by_spawn_id):
                iid = by_spawn_id[str(spawn_id)]
                reused_spawn_count += 1
                if len(reused_spawn_samples) < _SAMPLE_N:
                    reused_spawn_samples.append((spawn_id, iid))

            if iid is None:
                pos_key = f"{zone_norm}|{relx}|{rely}|{tid}"
                iid = by_pos_key.get(pos_key)
                if iid is not None:
                    reused_pos_count += 1
                    if len(reused_pos_samples) < _SAMPLE_N:
                        reused_pos_samples.append((pos_key, iid))

            if iid is None or iid in used_ids:
                iid = max_iid + 1
                max_iid = iid

                try:
                    img_dbg = getattr(b, 'image_path', None)
                except Exception:
                    img_dbg = None
                new_assigned_count += 1
                if len(new_assigned_samples) < _SAMPLE_N:
                    new_assigned_samples.append(iid)
                try:
                    logger.debug(f"[Buildings][SaveSplit] New ID assigned: iid={iid} zone={zone_norm} rel=({relx},{rely}) tpl={tid} img={img_dbg}")
                except Exception:
                    pass

            # Mark id as used in this pass
            try:
                used_ids.add(int(iid))
            except Exception:
                pass

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

    # Concise success line (paths at DEBUG to avoid console spam)
    logger.info(f"[Buildings][SaveSplit] Saved templates={len(templates_list)} instances={len(instances_out)}")
    try:
        logger.debug(f"✅ templates_path={t_path}")
        logger.debug(f"✅ instances_path={i_path}")
    except Exception:
        pass
    # Single concise summary for IDs used this save
    try:
        logger.info(
            f"[Buildings][SaveSplit] ID summary: preserved={preserved_count} reused_spawn={reused_spawn_count} reused_pos={reused_pos_count} new_assigned={new_assigned_count}"
        )
        # Optional tiny debug samples for traceability
        if preserved_samples:
            logger.debug(f"[Buildings][SaveSplit] preserved_samples={preserved_samples}")
        if reused_spawn_samples:
            logger.debug(f"[Buildings][SaveSplit] reused_spawn_samples={reused_spawn_samples}")
        if reused_pos_samples:
            logger.debug(f"[Buildings][SaveSplit] reused_pos_samples={reused_pos_samples}")
        if new_assigned_samples:
            logger.debug(f"[Buildings][SaveSplit] new_assigned_samples={new_assigned_samples}")
    except Exception:
        pass

    # Audit: diff previous vs new instances to detect added/removed/modified IDs
    try:
        def _as_id_map(arr: list[dict]) -> dict[int, dict]:
            out: dict[int, dict] = {}
            for e in arr or []:
                try:
                    eid = int(e.get('id'))
                except Exception:
                    continue
                out[eid] = e
            return out
        old_map = _as_id_map(existing_instances)
        new_map = _as_id_map(instances_out)
        old_ids = set(old_map.keys())
        new_ids = set(new_map.keys())
        added = sorted(new_ids - old_ids)
        removed = sorted(old_ids - new_ids)
        common = sorted(new_ids & old_ids)
        if added:
            logger.info(f"[Buildings][SaveSplit][Audit] Added IDs: {added}")
        if removed:
            logger.info(f"[Buildings][SaveSplit][Audit] Removed IDs: {removed}")
        # Field-level modifications for common IDs
        for iid in common:
            o = old_map.get(iid, {})
            n = new_map.get(iid, {})
            diffs = {}
            try:
                for key in ('template_id', 'zone', 'rel_x', 'rel_y'):
                    ov = o.get(key)
                    nv = n.get(key)
                    if ov != nv:
                        diffs[key] = {'old': ov, 'new': nv}
            except Exception:
                pass
            if diffs:
                logger.info(f"[Buildings][SaveSplit][Audit] Modified ID {iid}: {diffs}")
    except Exception:
        pass