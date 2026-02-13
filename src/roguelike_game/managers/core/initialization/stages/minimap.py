from __future__ import annotations

from roguelike_engine.minimap import Minimap

from ..types import InitContext


def init_minimap(ctx: InitContext) -> None:
    ctx.game.minimap = Minimap()
    # Wire minimap into ECS world so MinimapUpdateSystem can access it
    try:
        ctx.game.ecs.ecs_world.minimap = ctx.game.minimap
    except Exception:
        pass
