from __future__ import annotations

"""
Thin aggregator for initialization stages.

This module re-exports stage functions split across submodules under
`roguelike_game.managers.core.initialization.stages` to improve readability,
robustness, and scalability while keeping the public API stable.
"""

# Re-export types for convenience
from .types import InitContext

# Re-export all stage functions from the package
from .stages import (
    setup_display,
    setup_world,
    load_world_state,
    handle_deferred_levels,
    init_map,
    create_loader,
    init_state,
    dev_auto_import_buildings,
    init_buildings,
    init_z_layer,
    init_buildings_editor,
    init_tile_editor,
    init_map_editor,
    init_inventory_editor,
    init_entities_editor,
    init_spells_editor,
    init_spawner_editor,
    init_particles_editor,
    init_minimap,
    init_ecs,
    init_items,
    init_item_editor,
    init_renderer,
    init_menu,
    init_audio,
)

__all__ = [
    "InitContext",
    "setup_display",
    "setup_world",
    "load_world_state",
    "handle_deferred_levels",
    "init_map",
    "create_loader",
    "init_state",
    "dev_auto_import_buildings",
    "init_buildings",
    "init_z_layer",
    "init_buildings_editor",
    "init_tile_editor",
    "init_map_editor",
    "init_inventory_editor",
    "init_entities_editor",
    "init_spells_editor",
    "init_spawner_editor",
    "init_particles_editor",
    "init_minimap",
    "init_ecs",
    "init_items",
    "init_item_editor",
    "init_renderer",
    "init_menu",
    "init_audio",
]
