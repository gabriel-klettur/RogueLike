import os
import pygame
from roguelike_game.managers.items.loader import ItemsLoader


def load_items_and_icons(items_path: str):
    # Ignore items_path and load from SQLite
    items, _assets = ItemsLoader().load()
    pygame.font.init()
    icon_surfaces: dict[str, pygame.Surface | None] = {}
    for item_id, model in items.items():
        icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
        if isinstance(icon, list):
            icon = icon[0]
        surf = None
        if icon:
            path = os.path.join(os.getcwd(), icon)
            try:
                surf = pygame.image.load(path).convert_alpha()
            except Exception:
                surf = None
        icon_surfaces[item_id] = surf
    return items, icon_surfaces
