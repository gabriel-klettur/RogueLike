from __future__ import annotations

from typing import Optional, List, Dict, Any
import json
import os

from roguelike_engine.config.config import BUILDINGS_INSTANCES_PATH, BUILDINGS_TEMPLATES_PATH
import logging
_log = logging.getLogger(__name__)


def load_buildings_instances() -> List[Dict[str, Any]]:
    """Read buildings instances JSON. Always returns a list (possibly empty)."""
    path = BUILDINGS_INSTANCES_PATH
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


def write_buildings_instances(data: List[Dict[str, Any]]) -> None:
    """Write buildings instances JSON with indent and UTF-8. Creates parent dir."""
    path = BUILDINGS_INSTANCES_PATH
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Audit: load previous snapshot for diff logging
    try:
        _old = load_buildings_instances()
    except Exception:
        _old = []
    # Deduplicate by (zone, rel_x, rel_y, template_id) BUT do not merge entries linked to spawners.
    try:
        _before = len(data or [])
        def _key(e: Dict[str, Any]) -> str:
            try:
                zone = str(e.get('zone') or 'lobby')
                rx = int(e.get('rel_x') or 0)
                ry = int(e.get('rel_y') or 0)
                tid = int(e.get('template_id')) if e.get('template_id') is not None else -1
                return f"{zone}|{rx}|{ry}|{tid}"
            except Exception:
                return f"{e!r}"
        def _is_spawner_linked(e: Dict[str, Any]) -> bool:
            try:
                ov = e.get('overrides') if isinstance(e, dict) else None
                if isinstance(ov, dict) and (ov.get('_is_spawner_visual') or ov.get('spawner_instance_id')):
                    return True
                if str(e.get('spawner_instance_id') or '') or str(e.get('spawn_id') or ''):
                    return True
            except Exception:
                pass
            return False
        # Partition: protected (spawner-linked) vs normal
        protected: List[Dict[str, Any]] = []
        normal: List[Dict[str, Any]] = []
        for e in list(data or []):
            (protected if _is_spawner_linked(e) else normal).append(e)
        # Deduplicate only normal entries
        seen: dict[str, Dict[str, Any]] = {}
        # For normal entries, prefer lower id to keep oldest
        def _score_normal(e: Dict[str, Any]) -> int:
            try:
                return -int(e.get('id') or 0)
            except Exception:
                return 0
        # Gather duplicates for logging only among normals
        _dups: dict[str, list[Dict[str, Any]]] = {}
        for e in normal:
            k = _key(e)
            _dups.setdefault(k, []).append(e)
        for e in normal:
            k = _key(e)
            cur = seen.get(k)
            if cur is None or _score_normal(e) > _score_normal(cur):
                seen[k] = e
        dedup_normal = list(seen.values())
        # Drop normals that collide with any protected key
        prot_keys = { _key(e) for e in protected }
        dedup_normal = [e for e in dedup_normal if _key(e) not in prot_keys]
        # Combine back
        data = protected + dedup_normal
        _after = len(data)
        try:
            _log.debug(f"[BuildingsService] write_buildings_instances: dedup(normal) {len(normal)} -> {len(dedup_normal)}; protected kept={len(protected)}; total {_before}->{_after} (removed={_before-_after})")
            for k, candidates in _dups.items():
                if len(candidates) > 1:
                    try:
                        chosen = seen.get(k)
                        chosen_id = int(chosen.get('id')) if isinstance(chosen, dict) and chosen.get('id') is not None else None
                    except Exception:
                        chosen_id = None
                    ids = []
                    for c in candidates:
                        try:
                            ids.append(int(c.get('id')))
                        except Exception:
                            ids.append(c.get('id'))
                    _log.info(f"[BuildingsService][Dedup] key={k} candidates={ids} -> chosen={chosen_id}")
        except Exception:
            pass
        # Stable order by id if present
        try:
            data.sort(key=lambda x: int(x.get('id') or 0))
        except Exception:
            pass
    except Exception:
        # Best-effort: fall back to original data
        data = list(data or [])
    # Audit: compute diff old vs new (by id)
    try:
        def _as_id_map(arr: List[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
            out: Dict[int, Dict[str, Any]] = {}
            for e in arr or []:
                try:
                    eid = int(e.get('id'))
                except Exception:
                    continue
                out[eid] = e
            return out
        old_map = _as_id_map(_old)
        new_map = _as_id_map(data)
        old_ids = set(old_map.keys())
        new_ids = set(new_map.keys())
        added = sorted(new_ids - old_ids)
        removed = sorted(old_ids - new_ids)
        common = sorted(new_ids & old_ids)
        if added:
            _log.info(f"[BuildingsService][Audit] Added IDs: {added}")
        if removed:
            _log.info(f"[BuildingsService][Audit] Removed IDs: {removed}")
        # Detect field-level modifications for common IDs (core placement fields)
        modified: list[tuple[int, dict]] = []
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
                modified.append((iid, diffs))
        if modified:
            for iid, diffs in modified:
                _log.info(f"[BuildingsService][Audit] Modified ID {iid}: {diffs}")
    except Exception:
        pass
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data or [], f, ensure_ascii=False, indent=2)


def load_buildings_templates() -> List[Dict[str, Any]]:
    """Read buildings templates JSON. Always returns a list (possibly empty)."""
    path = BUILDINGS_TEMPLATES_PATH
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


def get_template_image_path(template_id: int) -> Optional[str]:
    """Return a preferred image path for a template id, trying assets.idle -> assets.image -> image."""
    for e in load_buildings_templates():
        try:
            if int(e.get('id')) == int(template_id):
                assets = e.get('assets') if isinstance(e.get('assets'), dict) else {}
                path = assets.get('idle') or assets.get('image') or e.get('image')
                return str(path) if path else None
        except Exception:
            continue
    return None
