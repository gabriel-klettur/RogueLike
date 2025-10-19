from __future__ import annotations

import logging
from types import SimpleNamespace

from roguelike_game.managers.z_layer import ZLayerManager

from ..types import InitContext

logger = logging.getLogger(__name__)


def init_z_layer(ctx: InitContext) -> None:
    g = ctx.game
    z = ZLayerManager(g.z_state)
    entities = SimpleNamespace(
        player=g.ecs.ecs_world.player_position,
        buildings=g.buildings.buildings,
    )
    z.initialize(g.state, entities)
    g.zlayer = z
    g.entities = entities
