from __future__ import annotations

import json
import os
import logging
from typing import Any, Dict, List, Optional

from roguelike_engine.config.config import BUILDINGS_INSTANCES_PATH, BUILDINGS_TEMPLATES_PATH

logger = logging.getLogger(__name__)


def load_buildings_instances_json() -> List[Dict[str, Any]]:
    try:
        with open(BUILDINGS_INSTANCES_PATH, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


def write_buildings_instances_json(arr: List[Dict[str, Any]]) -> None:
    try:
        arr.sort(key=lambda e: int(e.get('id') or 0))
    except Exception:
        pass
    os.makedirs(os.path.dirname(BUILDINGS_INSTANCES_PATH), exist_ok=True)
    with open(BUILDINGS_INSTANCES_PATH, 'w', encoding='utf-8') as f:
        json.dump(arr or [], f, ensure_ascii=False, indent=4)


def load_buildings_templates_json() -> List[Dict[str, Any]]:
    try:
        with open(BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


def get_template_image_path(templates: List[Dict[str, Any]], template_id: int) -> Optional[str]:
    for t in templates:
        try:
            if int(t.get('id')) == int(template_id):
                assets = t.get('assets') if isinstance(t.get('assets'), dict) else {}
                path = assets.get('idle') or assets.get('image') or t.get('image')
                return str(path) if path else None
        except Exception:
            continue
    return None
