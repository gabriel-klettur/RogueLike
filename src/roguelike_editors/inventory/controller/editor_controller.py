import pygame
import os
import json
from roguelike_ui.services.json_persistence import load_from_json, save_to_json
import logging
from roguelike_engine.config.config import DATA_DIR, PROJECT_ROOT

from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from roguelike_editors.inventory.view.editor_view import InventoryEditorView
from roguelike_editors.inventory.events.inventory_editor_events import InventoryEditorEventHandler
from roguelike_editors.inventory.controller.inventory_grid_controller import InventoryGridController
from roguelike_editors.inventory.events.inventory_grid_events import InventoryGridEventHandler

class InventoryEditorController:
    """
    Controller para el editor de inventario (MVC): maneja estados y eventos.
    """
    def __init__(self, world, assets: dict, font: pygame.font.Font):
        self.model = InventoryEditorModel()
        self.world = world
        self.assets = assets
        self.font = font
        self.view = InventoryEditorView(assets, font)
        self.event_handler = InventoryEditorEventHandler(self)
        # Controller para flujo Add/Delete items
        self.grid_controller = InventoryGridController(self)
        self.grid_event_handler = InventoryGridEventHandler(self.grid_controller)
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Paths por categoría

        self.paths = {
            'player': {
                'default': os.path.join(DATA_DIR, 'defaults', 'inventory_player.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'inventory_player.json'),
            },
            'monsters': {
                'default': os.path.join(DATA_DIR, 'defaults', 'inventory_monsters.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'inventory_monsters.json'),
            },
            'map': {
                'default': os.path.join(DATA_DIR, 'defaults', 'inventory_map.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'inventory_map.json'),
            },
        }
        # Cargar datos JSON en el modelo
        for cat, p in self.paths.items():
            self.model.default_data[cat] = load_from_json(p['default'])
            self.model.active_data[cat] = load_from_json(p['active'])
        # Cargar y validar esquemas JSON
        try:
            import jsonschema
            schemas_dir = os.path.join(PROJECT_ROOT, 'schemas', 'inventory')
            self.schemas = {}
            for cat_name, fname in [('player','InventoryPlayerSchema.json'),('monsters','InventoryMonstersSchema.json'),('map','InventoryMapSchema.json')]:
                schema_path = os.path.join(schemas_dir, fname)
                with open(schema_path, encoding='utf-8') as f:
                    self.schemas[cat_name] = json.load(f)
            # Validar default_data
            for c, data in self.model.default_data.items():
                try:
                    jsonschema.validate(data, self.schemas[c])
                except Exception as ve:
                    self.logger.warning(f"Default data for '{c}' does not conform to schema: {ve}")
            # Validar active_data entries
            for c, entries in self.model.active_data.items():
                if isinstance(entries, dict):
                    for key, entry in entries.items():
                        try:
                            jsonschema.validate(entry, self.schemas.get(c, {}))
                        except Exception as ve:
                            self.logger.warning(f"Active data entry '{key}' for '{c}' does not conform to schema: {ve}")
        except ImportError:
            self.logger.warning("jsonschema package not installed; skipping schema validation")

    def _save_default(self):
        cat = self.model.current_category
        path = self.paths[cat]['default']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            with open(path, 'w', encoding='utf-8') as f:
                json.dump(self.model.default_data.get(cat, {}), f, indent=2)
            self.logger.info(f"Default inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.logger.error(f"Error saving default inventory for '{cat}' to {path}: {e}")

    def _save_active(self):
        cat = self.model.current_category
        path = self.paths[cat]['active']
        try:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            # Guarda cada entrada de entidad con save_to_json
            for eid_str, entry in self.model.active_data.get(cat, {}).items():
                save_to_json(path, eid_str, entry)
            self.logger.info(f"Active inventory for '{cat}' saved to {path}")
        except Exception as e:
            self.logger.error(f"Error saving active inventory for '{cat}' to {path}: {e}")

    def handle_event(self, event):
        # Flujo Add/Delete en el grid
        if self.grid_event_handler.handle(event):
            return
        # Delega a manejador principal
        self.event_handler.handle(event)



    def draw(self, screen):
        self.view.draw(screen, self.model, self.world)
