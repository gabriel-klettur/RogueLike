from __future__ import annotations

import os
import json
from roguelike_engine.config import config
from roguelike_engine.config.map_config import global_map_settings


def _project_root() -> str:
    """Return absolute path to the project root (repo root), derived from this file's location.

    This anchors persistence to the repository root instead of the current working directory,
    ensuring load/save use e.g. d:/Python/RogueLike/data/... consistently.
    """
    here = os.path.abspath(os.path.dirname(__file__))
    # services/ -> spawner/ -> roguelike_editors/ -> src/ -> repo root
    return os.path.abspath(os.path.join(here, '..', '..', '..', '..'))


def _abs_data_base() -> str:
    base = getattr(config, 'DATA_DIR', 'data')
    if os.path.isabs(base):
        return base
    return os.path.join(_project_root(), base)


def _world_spawners_dir() -> str:
    try:
        return str(global_map_settings.worlds_dir / global_map_settings.current_world / 'spawners')
    except Exception:
        return os.path.join(_abs_data_base(), 'spawners')


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


def _preferred_spawners_dir() -> str:
    """Per-world only: always use data/worlds/<current_world>/spawners.
    Ensure the directory exists to allow save operations from the editor.
    """
    wdir = _world_spawners_dir()
    try:
        os.makedirs(wdir, exist_ok=True)
    except Exception:
        pass
    return wdir


def instances_path() -> str:
    base = _preferred_spawners_dir()
    return os.path.join(base, 'spawners_instances.json')


def spawners_path() -> str:
    # Templates are global (shared across worlds)
    base = _abs_data_base()
    return os.path.join(base, 'spawners', 'spawners_templates.json')
