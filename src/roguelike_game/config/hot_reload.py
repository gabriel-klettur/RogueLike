"""Centralized hot-reload API (wrapper).

This module re-exports the public hot-reload entry points from the
modular implementation under roguelike_game.config.hotreload.

Public functions:
- reload_all_data(game=None, force=False)
- reload_all_game_data(game, force=False)
- reload_changed_python_modules(force=False)
- reload_all_game_data_and_code(game, force=False)
"""
from __future__ import annotations

# Re-export the building blocks from the modular package
from .hotreload import (
    reload_all_data,
    reload_all_game_data,
    reload_changed_python_modules,
)
from .hotreload.orchestrator import run_reload as _run_reload


def reload_all_game_data_and_code(game, *, force: bool = False):
    """Wrapper that delegates to the modular orchestrator.

    Uses this module's functions so test monkeypatches here affect the flow.
    """
    return _run_reload(
        game,
        force=bool(force),
        code_reload=reload_changed_python_modules,
        data_reload=reload_all_game_data,
    )


__all__ = [
    "reload_all_data",
    "reload_all_game_data",
    "reload_changed_python_modules",
    "reload_all_game_data_and_code",
]
