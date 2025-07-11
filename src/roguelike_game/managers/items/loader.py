"""
Loader de ítems (data only): carga catálogo de ítems y sus assets.
"""
from pathlib import Path
from roguelike_game.ecs.components.item_models import load_items
from roguelike_engine.utils.loader import load_image

class ItemsLoader:
    """
    Carga ítems desde JSON junto con sus assets de iconos.
    """
    def load(self):
        items_path = Path('data') / 'items' / 'items.json'
        items = load_items(str(items_path))
        assets = {}
        for item_id, item in items.items():
            icon_paths = []
            if item.icon:
                icon_paths = item.icon if isinstance(item.icon, list) else [item.icon]
            else:
                if item.icon_small:
                    icon_paths.append(item.icon_small)
                if item.icon_large:
                    icon_paths.append(item.icon_large)
            if icon_paths:
                try:
                    assets[item_id] = load_image(icon_paths[0])
                except Exception as e:
                    print(f"[ItemsLoader] Error cargando icono {item_id}: {e}")
        return items, assets
