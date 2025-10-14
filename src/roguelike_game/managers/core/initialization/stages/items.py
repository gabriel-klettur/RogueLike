from __future__ import annotations

from roguelike_game.managers.items.loader import ItemsLoader

from ..types import InitContext


def init_items(ctx: InitContext) -> None:
    """Carga catálogo de ítems y assets de ítems para todo el juego"""
    loader = ItemsLoader()
    items, assets = loader.load()
    ctx.game.items = items
    ctx.game.item_assets = assets


from .editors import init_item_editor  # re-export helper
