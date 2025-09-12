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
    # Deduplicate by (zone, rel_x, rel_y, template_id)
    try:
        _before = len(data or [])
        seen: dict[str, Dict[str, Any]] = {}
        def _key(e: Dict[str, Any]) -> str:
            try:
                zone = str(e.get('zone') or 'lobby')
                rx = int(e.get('rel_x') or 0)
                ry = int(e.get('rel_y') or 0)
                tid = int(e.get('template_id')) if e.get('template_id') is not None else -1
                return f"{zone}|{rx}|{ry}|{tid}"
            except Exception:
                return f"{e!r}"
        def _score(e: Dict[str, Any]) -> tuple:
            # Prefer entries explicitly tagged as spawner visuals, then lower id to keep oldest
            ov = e.get('overrides') if isinstance(e, dict) else None
            is_spawn_vis = 1 if (isinstance(ov, dict) and bool(ov.get('_is_spawner_visual'))) else 0
            try:
                # negative so lower id wins on max()
                neg_id = -int(e.get('id') or 0)
            except Exception:
                neg_id = 0
            return (is_spawn_vis, neg_id)
        for e in list(data or []):
            k = _key(e)
            cur = seen.get(k)
            if cur is None:
                seen[k] = e
            else:
                # Keep the one with better score (spawner_visual preferred, then lower id)
                if _score(e) > _score(cur):
                    seen[k] = e
        data = list(seen.values())
        _after = len(data)
        try:
            _log.debug(f"[BuildingsService] write_buildings_instances: dedup {_before}->{_after} entries (removed={_before-_after})")
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
