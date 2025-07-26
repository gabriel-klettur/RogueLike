import pygame
import os
import json
import logging
from .data_controller import DataController

from roguelike_engine.config.config import DATA_DIR, PROJECT_ROOT
from roguelike_ui.services.json_persistence import load_from_json

from roguelike_editors.inventory.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_controller import InventoryItemsPanelController
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_events import ItemSelectionPanelEventHandler

from roguelike_editors.inventory.editor_events import InventoryEditorEventHandler
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_events import InventoryItemsPanelEventHandler
from roguelike_editors.inventory.left_panel.panel_event_handler import PanelEventHandler

from roguelike_editors.inventory.editor_model import InventoryEditorModel

from roguelike_editors.inventory.editor_view import InventoryEditorView


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
        # Load inventory JSON data via DataController
        self.data_controller = DataController(self.model)
        self.data_controller.load_data()


    def handle_event(self, event):
        """
        Maneja eventos orquestando todos los manejadores.
        """
        # Delegar completamente a InventoryEditorEventHandler
        self.event_handler.handle(event)

    def debug_dump(self):
        """
        [DEBUG][Controller] Volcado completo del estado del InventoryEditorController.
        """
        m = self.model
        print("[DEBUG][Controller] InventoryEditorController.debug_dump:")
        print(f"  visible: {m.visible}")
        print(f"  entities: {m.entities}")
        print(f"  selected_eid: {m.selected_eid}")
        print(f"  editing_property: {m.editing_property}")
        print(f"  editing_index: {m.editing_index}")
        print(f"  drag_item: {m.drag_item}")
        print(f"  drag_slot: {m.drag_slot}")
        print(f"  scroll_offset: {m.scroll_offset}")
        print(f"  left_panel_model: {m.left_panel_model}")
        print(f"  items_panel_model: {m.items_panel_model}")
        print(f"  item_selection_panel_model: {m.item_selection_panel_model}")
        print(f"  inventory_panel_controller: {self.inventory_panel_controller}")
        print(f"  grid_controller: {self.grid_controller}")
        print(f"  inventory_panel_view: {self.view.inventory_panel_view}")
        print(f"  grid_view: {self.view.grid_view}")
        print(f"  item_panel_view: {self.view.item_panel_view}")

    def draw(self, screen):
        """
        Dibuja la vista del editor.
        """
        self.view.draw(screen, self.model, self.world)
