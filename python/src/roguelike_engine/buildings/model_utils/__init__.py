"""Utilities for `BuildingModel` implementation.

This package contains helpers for image loading/caching, collision tile
construction, and pickling support. Separated to keep the main model lean
and focused on its public API.
"""

from .image_ops import load_and_prepare_image, build_full_mask  # re-export
from .collision_ops import build_collision_tiles  # re-export
from .pickling_ops import model_getstate, model_setstate  # re-export
