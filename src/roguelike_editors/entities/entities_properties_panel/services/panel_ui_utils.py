"""Utility helpers for Properties Panel UI cache and refresh.

These helpers centralize lightweight UI refresh and cache-reset operations so
controllers/services can remain focused on business logic.
"""
from __future__ import annotations

from typing import Optional

import pygame


def reset_grid_cache(grid_controller) -> None:
    """Reset memoized identifiers so the grid rebuilds on next draw."""
    try:
        grid_controller.model.last_entity_id = None
        grid_controller.model.last_state_tab = None
    except Exception:
        pass


def clear_thumbnail_cache(grid_controller) -> None:
    """Clear the thumbnail cache so images are reloaded freshly."""
    try:
        grid_controller.view.thumbnail_cache.clear()
    except Exception:
        pass


def request_render(editor_controller) -> None:
    """Ask the editor to re-render the current screen if possible."""
    try:
        editor_controller.render(editor_controller.game.screen)
    except Exception:
        pass
