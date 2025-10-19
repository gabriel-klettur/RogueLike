from __future__ import annotations

from roguelike_engine.minimap import Minimap

from ..types import InitContext


def init_minimap(ctx: InitContext) -> None:
    ctx.game.minimap = Minimap()
