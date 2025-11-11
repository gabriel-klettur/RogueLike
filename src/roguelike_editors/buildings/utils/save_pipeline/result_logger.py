"""Structured logging helpers for the save pipeline."""
from __future__ import annotations

import logging

from .models import AllocationStats, SaveResult

logger = logging.getLogger(__name__)


def log_summary(result: SaveResult) -> None:
    """Emit aggregated log lines summarising the save operation."""

    logger.info(
        "[Buildings][SaveSplit] Saved templates=%s instances=%s",
        result.templates_saved,
        result.instances_saved,
    )
    _log_allocation(result.allocation)


def _log_allocation(stats: AllocationStats) -> None:
    logger.info(
        "[Buildings][SaveSplit] ID summary: preserved=%s reused_spawn=%s reused_pos=%s new_assigned=%s",
        stats.preserved_count,
        stats.reused_spawn_count,
        stats.reused_position_count,
        stats.new_assigned_count,
    )
    if stats.preserved_samples:
        logger.debug("[Buildings][SaveSplit] preserved_samples=%s", stats.preserved_samples)
    if stats.reused_spawn_samples:
        logger.debug("[Buildings][SaveSplit] reused_spawn_samples=%s", stats.reused_spawn_samples)
    if stats.reused_position_samples:
        logger.debug("[Buildings][SaveSplit] reused_pos_samples=%s", stats.reused_position_samples)
    if stats.new_assigned_samples:
        logger.debug("[Buildings][SaveSplit] new_assigned_samples=%s", stats.new_assigned_samples)
