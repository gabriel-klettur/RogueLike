import pygame
import os
import json
import logging

from roguelike_engine.config.config import DATA_DIR, PROJECT_ROOT
from roguelike_ui.services.json_persistence import load_from_json

from roguelike_editors.inventory.controller.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.controller.right_panel.inventory_items_panel.inventory_items_panel_controller import InventoryItemsPanelController
from roguelike_editors.inventory.events.right_panel.item_selection_panel.item_selection_panel_events import ItemSelectionPanelEventHandler

from roguelike_editors.inventory.events.editor_events import InventoryEditorEventHandler
from roguelike_editors.inventory.events.right_panel.inventory_items_panel.inventory_items_panel_events import InventoryItemsPanelEventHandler
from roguelike_editors.inventory.events.left_panel import PanelEventHandler

from roguelike_editors.inventory.model.editor_model import InventoryEditorModel

from roguelike_editors.inventory.view.editor_view import InventoryEditorView


class InventoryEditorController:
    """
    Controller para el editor de inventario (MVC): maneja estados y eventos.
    """
    def __init__(self, game, world, assets: dict, font: pygame.font.Font):
        self.game = game
        self.model = InventoryEditorModel()
        self.world = world
        self.assets = assets
        self.font = font
        self.view = InventoryEditorView(assets, font)
        # Panel MVC para listado de entidades
        self.inventory_panel_controller = PanelController(self, self.view.inventory_panel_model)
        # Asociar controlador a la vista
        self.view.inventory_panel_controller = self.inventory_panel_controller
        self.inventory_panel_event_handler = PanelEventHandler(self, self.inventory_panel_controller, self.view.inventory_panel_view, self.view.inventory_panel_model)
        self.event_handler = InventoryEditorEventHandler(self)
        # Controller para flujo Add/Delete items
        self.grid_controller = InventoryItemsPanelController(self)
        self.grid_event_handler = InventoryItemsPanelEventHandler(self.grid_controller)
        self.item_selection_event_handler = ItemSelectionPanelEventHandler(self.grid_controller, self.view.item_panel_controller, self.view.item_panel_view)
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        # Paths por categoría

        self.paths = {
            'player': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_player.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_player.json'),
            },
            'monsters': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_monsters.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_monsters.json'),
            },
            'map': {
                'default': os.path.join(DATA_DIR, 'inventory', 'defaults', 'inventory_map.json'),
                'active': os.path.join(DATA_DIR, 'inventory', 'active', 'inventory_map.json'),
            },
        }
        # Cargar datos JSON en el modelo
        for cat, p in self.paths.items():
            # Cargar default y active
            self.model.default_data[cat] = load_from_json(p['default'])
            loaded_active = load_from_json(p['active'])
            # Desanidar caso de map anidado incorrectly bajo 'map'
            if cat == 'map' and isinstance(loaded_active, dict) and 'map' in loaded_active:
                loaded_active = loaded_active['map']
                # reescribir archivo para corregir formato
                os.makedirs(os.path.dirname(p['active']), exist_ok=True)
                with open(p['active'], 'w', encoding='utf-8') as f:
                    json.dump(loaded_active, f, ensure_ascii=False, indent=2)
            self.model.active_data[cat] = loaded_active

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


    def handle_event(self, event):
        """
        Maneja eventos delegando a los manejadores específicos.
        """
        # Panel listado events
        if self.inventory_panel_event_handler.handle(event):
            return
        # Item selection panel events
        if self.item_selection_event_handler.handle(event):
            return
        # Flujo Add/Delete en el grid
        if self.grid_event_handler.handle(event):
            return
        # Delega a manejador principal
        self.event_handler.handle(event)

    def draw(self, screen):
        """
        Dibuja la vista del editor.
        """
        self.view.draw(screen, self.model, self.world)
