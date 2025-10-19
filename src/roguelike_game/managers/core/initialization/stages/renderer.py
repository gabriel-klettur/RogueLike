from __future__ import annotations

from roguelike_game.managers.core.render.render_manager import RendererManager

from ..types import InitContext


def init_renderer(ctx: InitContext) -> None:
    g = ctx.game
    if not hasattr(g, "map_editor"):
        # Import here to avoid circular imports at module load time
        from .editors import init_map_editor

        init_map_editor(ctx)
    g.renderer = RendererManager(
        g.screen,
        g.camera,
        g.map,
        g.entities,
        g.buildings_editor,
        g.tiles_editor,
        g.map_editor,
        g.perf_log,
        g.minimap,
        g.ecs,
    )
