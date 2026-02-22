"""FSM persistence public API facade.

This module intentionally keeps a small surface and delegates the heavy lifting
(IO, normalization, validation, linting, exports) to submodules under the same
package. All previous public functions are preserved for compatibility.
"""
from __future__ import annotations
from typing import Any, Dict, Tuple, List
from pathlib import Path

# Re-export the public API from the refactored modules via api.py
from .api import (
    # paths
    default_sets_path,
    default_assignments_path,
    default_layouts_path,
    default_schema_path,
    default_animation_map_path,
    default_ids_path,
    # operations
    load_sets,
    save_sets,
    get_last_lint,
    get_last_lint_enriched,
    load_assignments,
    save_assignments,
    load_animation_map,
    save_animation_map,
    load_layouts,
    save_layouts,
    validate,
    load_all,
    _generate_code_ids,
    # feature toggles
    AUTO_INCLUDE_DAMAGE,
)


__all__ = [
    "default_sets_path",
    "default_assignments_path",
    "default_layouts_path",
    "default_schema_path",
    "default_animation_map_path",
    "default_ids_path",
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
    "AUTO_INCLUDE_DAMAGE",
]
