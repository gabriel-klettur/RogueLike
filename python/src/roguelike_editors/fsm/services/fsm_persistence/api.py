from __future__ import annotations
from typing import Any, Dict, Tuple, List
from pathlib import Path

# Public paths
from .paths import (
    default_sets_path,
    default_assignments_path,
    default_layouts_path,
    default_schema_path,
    default_animation_map_path,
    default_ids_path,
)

# Loaders / Savers
from .loaders import (
    load_sets,
    load_assignments,
    load_animation_map,
    load_layouts,
)
from .savers import (
    save_assignments,
    save_animation_map,
    save_layouts,
)

# Validation
from .validate_json import validate

# Core service (lint cache, save_sets, load_all)
from .service_core import (
    save_sets,
    load_all,
    get_last_lint,
    get_last_lint_enriched,
)

# Exports / legacy
from .exports import _generate_code_ids

# Feature toggles for compatibility
from .normalize import AUTO_INCLUDE_DAMAGE

__all__ = [
    # paths
    "default_sets_path",
    "default_assignments_path",
    "default_layouts_path",
    "default_schema_path",
    "default_animation_map_path",
    "default_ids_path",
    # operations
    "load_sets",
    "save_sets",
    "get_last_lint",
    "get_last_lint_enriched",
    "load_assignments",
    "save_assignments",
    "load_animation_map",
    "save_animation_map",
    "load_layouts",
    "save_layouts",
    "validate",
    "load_all",
    "_generate_code_ids",
    # feature toggles
    "AUTO_INCLUDE_DAMAGE",
]
