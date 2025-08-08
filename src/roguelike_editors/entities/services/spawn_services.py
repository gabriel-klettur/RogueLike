"""Spawn helpers for Entities editor.
"""
from __future__ import annotations

from roguelike_game.factories.registry import get_factory


def spawn_entity(game, etype: str, tx: int, ty: int, player_stats: dict) -> int:
    """Spawn a player or monster entity at the given tile position.

    Decides factory based on whether the type exists in player_stats.
    """
    if etype in player_stats:
        return get_factory("player").create(game.ecs.ecs_world, tile_x=tx, tile_y=ty, class_player=etype)
    else:
        return get_factory("monster").create(game.ecs.ecs_world, tile_x=tx, tile_y=ty, monster_type=etype)
