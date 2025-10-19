from __future__ import annotations

from roguelike_engine.utils.loading_screen import LoadingScreen

from ..types import InitContext


def create_loader(ctx: InitContext) -> None:
    ctx.game.loader = LoadingScreen(ctx.screen, ctx.loading_bg)
