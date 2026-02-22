import os
import json
import pygame
from roguelike_game.managers.items.loader import ItemsLoader


def load_items_and_icons(items_path: str):
    # Ignore items_path and load from SQLite
    items, _assets = ItemsLoader().load()
    pygame.font.init()
    icon_surfaces: dict[str, pygame.Surface | None] = {}
    # Fallback index for icons missing in DB: scan assets folder once
    assets_root = os.path.join(os.getcwd(), 'assets')
    filename_to_path: dict[str, str] = {}
    try:
        for root, _dirs, files in os.walk(assets_root):
            for fn in files:
                if fn.lower().endswith('.png'):
                    # Keep first occurrence only; structure uses unique names by convention
                    filename_to_path.setdefault(fn.lower(), os.path.join(root, fn))
    except Exception:
        filename_to_path = {}
    # Optional explicit overrides: data/items/icon_overrides.json => { item_id: path }
    overrides_path = os.path.join(os.getcwd(), 'data', 'items', 'icon_overrides.json')
    overrides: dict[str, str] = {}
    try:
        if os.path.exists(overrides_path):
            with open(overrides_path, 'r', encoding='utf-8') as f:
                data = json.load(f)
                if isinstance(data, dict):
                    overrides = {str(k).lower(): str(v) for k, v in data.items()}
    except Exception:
        overrides = {}
    for item_id, model in items.items():
        # 0) Override explícito
        surf = None
        ov_path = overrides.get(str(item_id).lower())
        if ov_path:
            try:
                ov_full = ov_path if os.path.isabs(ov_path) else os.path.join(os.getcwd(), ov_path)
                if os.path.exists(ov_full):
                    surf = pygame.image.load(ov_full).convert_alpha()
            except Exception:
                surf = None
        # 1) DB-provided icon if no override took effect
        if surf is None:
            icon = getattr(model, 'icon_small', None) or getattr(model, 'icon', None)
            if isinstance(icon, list):
                icon = icon[0]
            if icon:
                path = os.path.join(os.getcwd(), icon)
                try:
                    surf = pygame.image.load(path).convert_alpha()
                except Exception:
                    surf = None
        # Fallback: look up <item_id>.png anywhere under assets
        if surf is None:
            try:
                candidate = filename_to_path.get(f"{str(item_id).lower()}.png")
                if candidate and os.path.exists(candidate):
                    surf = pygame.image.load(candidate).convert_alpha()
            except Exception:
                surf = None
        icon_surfaces[item_id] = surf
    return items, icon_surfaces
