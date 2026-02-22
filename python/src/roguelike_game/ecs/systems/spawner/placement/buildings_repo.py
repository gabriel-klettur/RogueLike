from __future__ import annotations

import json
import os
import logging
from typing import Any, Dict, List, Optional

import roguelike_engine.config.config as cfg

logger = logging.getLogger(__name__)


def load_buildings_instances_json() -> List[Dict[str, Any]]:
    try:
        with open(cfg.BUILDINGS_INSTANCES_PATH, 'r', encoding='utf-8-sig') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


def write_buildings_instances_json(arr: List[Dict[str, Any]]) -> None:
    # Sanitize: remove redundant overrides['spawner_instance_id'] (root field is source of truth)
    try:
        for e in arr or []:
            try:
                ov = e.get('overrides')
                if isinstance(ov, dict) and 'spawner_instance_id' in ov:
                    ov.pop('spawner_instance_id', None)
            except Exception:
                continue
    except Exception:
        pass
    try:
        arr.sort(key=lambda e: int(e.get('id') or 0))
    except Exception:
        pass
    os.makedirs(os.path.dirname(cfg.BUILDINGS_INSTANCES_PATH), exist_ok=True)
    with open(cfg.BUILDINGS_INSTANCES_PATH, 'w', encoding='utf-8') as f:
        json.dump(arr or [], f, ensure_ascii=False, indent=4)


def load_buildings_templates_json() -> List[Dict[str, Any]]:
    try:
        with open(cfg.BUILDINGS_TEMPLATES_PATH, 'r', encoding='utf-8-sig') as f:
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
