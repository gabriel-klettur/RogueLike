"""Compatibility layer for the buildings split save pipeline."""
from __future__ import annotations

import logging
from typing import Iterable, Optional

from roguelike_engine.config.config import (
    BUILDINGS_INSTANCES_PATH,
    BUILDINGS_TEMPLATES_PATH,
)

from .save_pipeline import run_save_pipeline

logger = logging.getLogger(__name__)

__all__ = ["save_buildings_to_json", "save_buildings_split"]


def save_buildings_to_json(
    buildings: Iterable[object],
    filepath: Optional[str] = None,
    *,
    z_state: Optional[object] = None,
    zone_offsets: Optional[dict] = None,
    **_: object,
):
    """Deprecated façade kept for legacy callers.

    The parameters mirror the original signature, but persistence is delegated to
    ``save_buildings_split`` which writes the canonical split files.
    """

    if filepath:
        logger.debug("[Buildings][SaveSplit] Ignoring legacy filepath=%s", filepath)
    logger.warning(
        "[Buildings][Deprecated] save_buildings_to_json() delega a save_buildings_split(); usa el modo split",
    )
    return save_buildings_split(
        buildings,
        z_state=z_state,
        zone_offsets=zone_offsets,
    )


def save_buildings_split(
    buildings: Iterable[object],
    *,
    z_state: Optional[object] = None,
    zone_offsets: Optional[dict] = None,
    templates_path: Optional[str] = None,
    instances_path: Optional[str] = None,
):
    """Persist buildings using the modular save pipeline."""

    result = run_save_pipeline(
        buildings,
        z_state=z_state,
        zone_offsets=zone_offsets,
        templates_path=templates_path or BUILDINGS_TEMPLATES_PATH,
        instances_path=instances_path or BUILDINGS_INSTANCES_PATH,
    )
    return result