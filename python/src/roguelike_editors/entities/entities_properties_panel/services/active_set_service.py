"""Service: handle toggling of active_set for players and monsters."""
from __future__ import annotations

import importlib
import logging

import roguelike_game.config.players_config as pc
from .panel_ui_utils import reset_grid_cache, clear_thumbnail_cache, request_render
from roguelike_editors.entities.entities_properties_panel.services.ecs_update_service import (
    update_player_assets,
    update_monster_assets,
)

logger = logging.getLogger(__name__)


def handle_active_set_toggled(controller, ent_id: str) -> None:
    """React to active_set change: reset grid caches and update ECS entities."""
    reset_grid_cache(controller.grid_controller)
    clear_thumbnail_cache(controller.grid_controller)

    try:
        ecs_world = controller.editor_controller.game.ecs.ecs_world
        if ent_id in controller.model.player_stats:
            try:
                importlib.reload(pc)
            except Exception:
                pass
            update_player_assets(ecs_world, ent_id)
            logger.debug("Player ECS entities updated for class %s after active_set toggle", ent_id)
        else:
            update_monster_assets(ecs_world, ent_id)
            logger.debug("Hostile ECS entities updated for type %s after active_set toggle", ent_id)
    except Exception as e:
        logging.error(
            "[ERROR][PropertiesPanel] Error updating ECS entities on active_set toggle for %s: %s",
            ent_id,
            e,
        )

    request_render(controller.editor_controller)
