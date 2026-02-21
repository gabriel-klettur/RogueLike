from __future__ import annotations

import json
import os
import logging
from typing import Any, Dict, List

from roguelike_engine.config import config
from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def _world_spawners_dir() -> str:
    try:
        wdir = global_map_settings.worlds_dir / global_map_settings.current_world / 'spawners'
        return str(wdir)
    except Exception:
        return os.path.join(config.DATA_DIR, 'spawners')


def _world_file(fname: str) -> str:
    return os.path.join(_world_spawners_dir(), fname)


def _global_file(fname: str) -> str:
    return os.path.join(config.DATA_DIR, 'spawners', fname)


def _has_nonempty_json(path: str) -> bool:
    if not os.path.exists(path):
        return False
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if isinstance(data, list):
            return len(data) > 0
        if isinstance(data, dict):
            return len(data.keys()) > 0
        return False
    except Exception:
        return False


def load_templates() -> Dict[str, Dict[str, Any]]:
    """Load spawner templates keyed by id.

    Accepts list format and normalizes to {id: template}.
    """
    # Global-only: data/spawners/spawners_templates.json
    path = _global_file('spawners_templates.json')
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
    # Global-only: data/spawners/spawners_waves.json
    path = _global_file('spawners_waves.json')
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
    world_path = _world_file('spawners_instances.json')
    if not os.path.exists(world_path):
        return []
    path = world_path
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
