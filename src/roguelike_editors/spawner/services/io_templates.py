from __future__ import annotations

from typing import Any, Dict, List
import json
import os
import logging

from .paths import spawners_path

logger = logging.getLogger(__name__)


def load_spawners_json() -> List[Dict[str, Any]]:
    path = spawners_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return []
        # Sanitize legacy fields
        for sp in data:
            try:
                if isinstance(sp, dict):
                    sp.pop('spawner_img', None)
                    sp.pop('spawner_img_size', None)
                    # Normalize building_id to int if possible
                    if sp.get('building_id') is not None:
                        try:
                            sp['building_id'] = int(sp['building_id'])
                        except (ValueError, TypeError):
                            pass
            except (AttributeError, KeyError, TypeError):
                continue
        return data
    except FileNotFoundError:
        return []
    except json.JSONDecodeError:
        logger.debug("load_spawners_json: JSON decode error", exc_info=True)
        return []
    except OSError:
        logger.debug("load_spawners_json: OS error while reading file", exc_info=True)
        return []


def write_spawners_json(data: List[Dict[str, Any]]) -> None:
    """Write the full spawners list to data/spawners/spawners_templates.json."""
    path = spawners_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Sanitize legacy fields before persisting
    cleaned: List[Dict[str, Any]] = []
    for sp in data or []:
        if not isinstance(sp, dict):
            continue
        sp2 = dict(sp)
        sp2.pop('spawner_img', None)
        sp2.pop('spawner_img_size', None)
        # Normalize building_id
        if sp2.get('building_id') is not None:
            try:
                sp2['building_id'] = int(sp2['building_id'])
            except (ValueError, TypeError):
                pass
        cleaned.append(sp2)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(cleaned, f, ensure_ascii=False, indent=2)
    logger.debug(f"[spawner.persistence] write_spawners_json: wrote {len(cleaned)} templates to {path}")


def save_spawner_template(updated: Dict[str, Any]) -> None:
    """Update or append a single spawner template in spawners_templates.json by id.

    If an entry with the same 'id' exists, replace it in-place; otherwise append it.
    """
    sid = str(updated.get('id')) if isinstance(updated, dict) else None  # type: ignore
    data = load_spawners_json()
    replaced = False
    if sid:
        for i, sp in enumerate(data):
            try:
                if str(sp.get('id')) == sid:
                    data[i] = updated
                    replaced = True
                    break
            except (AttributeError, TypeError, ValueError):
                continue
    if not replaced:
        data.append(updated)
    write_spawners_json(data)
