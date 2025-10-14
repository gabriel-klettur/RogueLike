from __future__ import annotations

import json
import os
import logging
from typing import Any, Dict, List

from roguelike_engine.config import config

logger = logging.getLogger(__name__)


def load_templates() -> Dict[str, Dict[str, Any]]:
    """Load spawner templates keyed by id.

    Accepts list format and normalizes to {id: template}.
    """
    base = config.DATA_DIR
    path = os.path.join(base, "spawners", "spawners_templates.json")
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if isinstance(data, list):
            return {t["id"]: t for t in data}
        return data or {}
    except FileNotFoundError:
        return {}


def load_waves() -> Dict[str, List[Dict[str, Any]]]:
    """Load wave sets by ID from spawners_waves.json.

    Supports either { id: [ ...waves... ] } or { id: { "waves": [ ... ] } } formats.
    """
    base = config.DATA_DIR
    path = os.path.join(base, "spawners", "spawners_waves.json")
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        return {}
    except json.JSONDecodeError:
        logger.warning("[SpawnerPlacementSystem] spawners_waves.json invalid JSON; ignoring")
        return {}
    if not isinstance(data, dict):
        return {}
    waves_map: Dict[str, List[Dict[str, Any]]] = {}
    for key, val in data.items():
        if isinstance(val, list):
            waves_map[key] = [w for w in val if isinstance(w, dict)]
        elif isinstance(val, dict) and isinstance(val.get("waves"), list):
            waves_map[key] = [w for w in val.get("waves", []) if isinstance(w, dict)]
    return waves_map


def load_instances() -> List[Dict[str, Any]]:
    base = config.DATA_DIR
    path = os.path.join(base, "spawners", "spawners_instances.json")
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        out = data if isinstance(data, list) else []
        try:
            if getattr(config, 'DEBUG_SPAWNER', False):
                num = len(out)
                with_vis = sum(1 for e in out if isinstance(e.get('visuals'), dict) and len(e.get('visuals') or {}) > 0)
                logger.debug(f"[SpawnerPlacementSystem] _load_instances: read {num} entries (visuals>0 in {with_vis}) from {path}")
        except Exception:
            pass
        return out
    except FileNotFoundError:
        return []
