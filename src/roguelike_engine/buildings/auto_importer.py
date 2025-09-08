import os
import json
import fnmatch
from pathlib import Path
from typing import List, Dict, Any, Tuple

# Use engine config paths
try:
    from roguelike_engine.config import config
except Exception as e:
    raise RuntimeError(f"Cannot import engine config: {e}")

ASSETS_DIR = Path(config.ASSETS_DIR)
PROJECT_ROOT = Path(config.PROJECT_ROOT)
TEMPLATES_PATH = Path(config.BUILDINGS_TEMPLATES_PATH)
INSTANCES_PATH = Path(config.BUILDINGS_INSTANCES_PATH)

EXCLUDES = list(getattr(config, 'DEV_AUTO_IMPORT_EXCLUDES', []) or [])
CREATE_INSTANCES = bool(getattr(config, 'DEV_AUTO_IMPORT_CREATE_INSTANCES', False))
DEFAULT_ZONE = getattr(config, 'DEV_AUTO_IMPORT_DEFAULT_ZONE', 'no zone')
DEFAULT_REL_POS = tuple(getattr(config, 'DEV_AUTO_IMPORT_DEFAULT_REL_POS', (0, 0)) or (0, 0))


def _normalize_asset_path(p: str) -> str:
    try:
        q = str(p).replace('\\', '/')
        while '//' in q:
            q = q.replace('//', '/')
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return str(p)


def _is_excluded(rel_path: str) -> bool:
    # Evaluate against fnmatch patterns with forward slashes
    for pat in EXCLUDES:
        try:
            if fnmatch.fnmatch(rel_path, pat):
                return True
        except Exception:
            continue
    return False


def _scan_assets() -> List[str]:
    """Return list of normalized asset paths under assets/buildings ending with .png/.PNG."""
    results: List[str] = []
    if not ASSETS_DIR.exists():
        return results
    for root, _dirs, files in os.walk(ASSETS_DIR):
        for name in files:
            low = name.lower()
            if not (low.endswith('.png') or low.endswith('.webp')):
                continue
            full = Path(root) / name
            try:
                rel = _normalize_asset_path(os.path.relpath(full, PROJECT_ROOT))
            except Exception:
                rel = _normalize_asset_path(str(full))
            # Only consider under assets/buildings/
            if not rel.startswith('assets/buildings/'):
                continue
            if _is_excluded(rel):
                continue
            results.append(rel)
    return sorted(results)


def _read_json_list(path: Path) -> List[Dict[str, Any]]:
    if not path.exists():
        return []
    try:
        with path.open('r', encoding='utf-8-sig') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except Exception:
        return []


def _write_json_list(path: Path, data: List[Dict[str, Any]]):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('w', encoding='utf-8') as f:
        json.dump(data, f, indent=4)


def _next_id(entries: List[Dict[str, Any]], key: str = 'id') -> int:
    mx = 0
    for e in entries:
        try:
            v = int(e.get(key))
            if v > mx:
                mx = v
        except Exception:
            continue
    return mx + 1


def _existing_idle_set(templates: List[Dict[str, Any]]) -> set:
    s = set()
    for t in templates:
        try:
            assets = t.get('assets') or {}
            idle = assets.get('idle') if isinstance(assets, dict) else None
            if idle:
                s.add(_normalize_asset_path(idle))
        except Exception:
            continue
    return s


def _make_template_entry(tid: int, idle_path: str) -> Dict[str, Any]:
    # Sensible defaults; collision scope CG (global by image), solid True, split_ratio 0.5
    return {
        'id': int(tid),
        'assets': {'idle': _normalize_asset_path(idle_path)},
        'solid': True,
        'split_ratio': 0.5,
        'collider_scope': 'CG',
    }


def _make_instance_entry(iid: int, template_id: int) -> Dict[str, Any]:
    rx, ry = DEFAULT_REL_POS if isinstance(DEFAULT_REL_POS, (list, tuple)) and len(DEFAULT_REL_POS) == 2 else (0, 0)
    return {
        'id': int(iid),
        'template_id': int(template_id),
        'zone': str(DEFAULT_ZONE),
        'rel_x': int(rx),
        'rel_y': int(ry),
    }


def auto_import_building_templates() -> Tuple[int, int]:
    """Scan assets/buildings and create templates (and optionally instances) for new images.
    Returns (created_templates, created_instances).
    """
    assets = _scan_assets()
    if not assets:
        return 0, 0

    templates = _read_json_list(TEMPLATES_PATH)
    instances = _read_json_list(INSTANCES_PATH) if CREATE_INSTANCES else []

    existing = _existing_idle_set(templates)

    created_t = 0
    created_i = 0

    next_tid = _next_id(templates)
    next_iid = _next_id(instances) if CREATE_INSTANCES else None

    for rel in assets:
        if rel in existing:
            continue
        # Create new template for this asset
        t_entry = _make_template_entry(next_tid, rel)
        templates.append(t_entry)
        existing.add(rel)
        created_t += 1

        if CREATE_INSTANCES:
            i_entry = _make_instance_entry(next_iid, next_tid)
            instances.append(i_entry)
            created_i += 1
            next_iid = (next_iid or 0) + 1

        next_tid += 1

    if created_t > 0:
        _write_json_list(TEMPLATES_PATH, templates)
        if CREATE_INSTANCES:
            _write_json_list(INSTANCES_PATH, instances)

    return created_t, created_i


def run(verbose: bool = True):
    ct, ci = auto_import_building_templates()
    if verbose:
        if ct or ci:
            print(f"[AutoImporter] Created templates={ct}, instances={ci}")
        else:
            print("[AutoImporter] No new assets to import")


if __name__ == '__main__':
    run(verbose=True)
