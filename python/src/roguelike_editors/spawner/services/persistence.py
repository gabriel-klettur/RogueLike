"""Spawner persistence facade.

This module re-exports a stable public API for spawner persistence, delegating the
implementation to cohesive submodules under the same package:

- paths: file and directory resolution
- ids: id generation and normalization
- io_instances/io_templates: read/write and sanitation of JSON files
- search: lookups and queries on persisted data
- operations: higher-level mutations and consistency operations
- zones: zone utilities

Keeping this facade maintains backward compatibility while allowing each concern to
evolve independently and be tested in isolation.
"""

from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple

from .paths import (
    _project_root,
    _abs_data_base,
    instances_path,
    spawners_path,
)
from . import paths as paths
from .ids import (
    slugify,
    generate_instance_id,
    ensure_instance_ids,
)
from .zones import zone_for_global_tile
from .io_templates import (
    load_spawners_json,
    write_spawners_json,
    save_spawner_template,
)
from .io_instances import (
    load_instances_json,
    write_instances_json,
)
from .search import (
    find_instance_by_id,
    find_instance_in_json,
)
from .operations import (
    rename_spawner_template_id,
    persist_drop,
    remove_visual_refs_by_building_id,
)

__all__ = [
    # paths
    "paths",
    "_project_root",
    "_abs_data_base",
    "instances_path",
    "spawners_path",
    # zones
    "zone_for_global_tile",
    # templates IO
    "load_spawners_json",
    "write_spawners_json",
    "save_spawner_template",
    # instances IO
    "load_instances_json",
    "write_instances_json",
    # ids
    "slugify",
    "generate_instance_id",
    "ensure_instance_ids",
    # search
    "find_instance_by_id",
    "find_instance_in_json",
    # operations
    "rename_spawner_template_id",
    "persist_drop",
    "remove_visual_refs_by_building_id",
]
