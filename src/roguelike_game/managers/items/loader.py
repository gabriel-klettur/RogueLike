"""
Loader de ítems (data only): carga catálogo de ítems y sus assets.
"""
from pathlib import Path
from roguelike_game.ecs.components.item_models import load_items
from roguelike_engine.utils.loader import load_image
import json
import jsonschema
from jsonschema import Draft7Validator, RefResolver

class ItemsLoader:
    """
    Carga ítems desde JSON junto con sus assets de iconos.
    """
    def load(self):
        items_path = Path('data') / 'items' / 'items.json'
        # Validar esquema de definiciones de ítems
        schema_path = Path('schemas') / 'items' / 'definitions.json'
        with open(schema_path, 'r', encoding='utf-8') as sf:
            definitions_schema = json.load(sf)
        with open(items_path, 'r', encoding='utf-8') as f:
            items_data = json.load(f)
        # Validar esquema de definiciones de ítems con Draft7Validator y RefResolver
        schema_uri = schema_path.resolve().as_uri()
        resolver = RefResolver(base_uri=schema_uri, referrer=definitions_schema)
        validator = Draft7Validator(definitions_schema, resolver=resolver)
        validator.validate(items_data)
        # Carga de modelos de ítems usando Pydantic
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
