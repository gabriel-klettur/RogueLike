from __future__ import annotations

from roguelike_game.managers.buildings import BuildingsManager

from ..types import InitContext


def init_buildings(ctx: InitContext) -> None:
    ctx.game.buildings = BuildingsManager(ctx.game.z_state, ctx.game.map)
