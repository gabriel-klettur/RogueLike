from __future__ import annotations

import os
from roguelike_engine.config import config


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


def instances_path() -> str:
    base = _abs_data_base()
    return os.path.join(base, 'spawners', 'spawners_instances.json')


def spawners_path() -> str:
    base = _abs_data_base()
    return os.path.join(base, 'spawners', 'spawners_templates.json')
