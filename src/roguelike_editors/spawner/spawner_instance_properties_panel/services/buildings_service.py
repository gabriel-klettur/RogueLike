from __future__ import annotations

from typing import Optional, List, Dict, Any
import json
import os

from roguelike_engine.config.config import BUILDINGS_INSTANCES_PATH, BUILDINGS_TEMPLATES_PATH


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
