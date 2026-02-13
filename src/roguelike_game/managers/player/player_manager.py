"""
Module: player_manager.py
Thin facade that enqueues a ClassChangeRequest for the ECS ClassChangeSystem.
"""
import logging

from roguelike_game.ecs.components.core.class_change_request import ClassChangeRequest

logger = logging.getLogger(__name__)


class PlayerManager:
    """Manage runtime player operations such as class change."""

    def __init__(self, ecs_world):
        self.ecs_world = ecs_world

    def change_class(self, new_class: str):
        """Enqueue a class-change request; ClassChangeSystem will apply it next tick."""
        eid = self.ecs_world.player_entity
        self.ecs_world.components.setdefault('ClassChangeRequest', {})[eid] = ClassChangeRequest(new_class)
        logger.info("[PlayerManager] Enqueued ClassChangeRequest('%s') for eid=%d", new_class, eid)
