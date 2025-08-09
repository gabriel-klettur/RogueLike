import pygame
import logging
logger = logging.getLogger(__name__)
from .data_controller import DataController



from roguelike_editors.inventory.left_panel.panel_controller import PanelController
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_controller import InventoryItemsPanelController
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_events import ItemSelectionPanelEventHandler

from roguelike_editors.inventory.editor_events import InventoryEditorEventHandler
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_events import InventoryItemsPanelEventHandler
from roguelike_editors.inventory.left_panel.panel_event_handler import PanelEventHandler

from roguelike_editors.inventory.editor_model import InventoryEditorModel

from roguelike_editors.inventory.editor_view import InventoryEditorView
from roguelike_editors.inventory.inventory_title.inventory_title_controller import InventoryTitleController
from roguelike_editors.inventory.inventory_title.inventory_title_model import InventoryTitleModel


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
        # Título del editor (MVC)
        self.title_model = InventoryTitleModel()
        self.title_controller = InventoryTitleController(self, self.title_model, self.font)
        # Asociar title controller a la vista para que renderice y obtenga el rect
        self.view.title_controller = self.title_controller
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
        # Exponer rutas de JSON para controladores hijos (p.ej., SaveController)
        self.paths = self.data_controller.paths


    def handle_event(self, event):
        """
        Maneja eventos orquestando todos los manejadores.
        """
        # Delegar completamente a InventoryEditorEventHandler
        self.event_handler.handle(event)

    def debug_dump(self):
        """
         Volcado completo del estado del InventoryEditorController.
        """
        m = self.model
        logger.debug(" InventoryEditorController.debug_dump:")
        logger.debug(f"  visible: {m.visible}")
        logger.debug(f"  entities: {m.entities}")
        logger.debug(f"  selected_eid: {m.selected_eid}")
        logger.debug(f"  editing_property: {m.editing_property}")
        logger.debug(f"  editing_index: {m.editing_index}")
        logger.debug(f"  drag_item: {m.drag_item}")
        logger.debug(f"  drag_slot: {m.drag_slot}")
        logger.debug(f"  scroll_offset: {m.scroll_offset}")
        logger.debug(f"  left_panel_model: {m.left_panel_model}")
        logger.debug(f"  items_panel_model: {m.items_panel_model}")
        logger.debug(f"  item_selection_panel_model: {m.item_selection_panel_model}")
        logger.debug(f"  inventory_panel_controller: {self.inventory_panel_controller}")
        logger.debug(f"  grid_controller: {self.grid_controller}")
        logger.debug(f"  inventory_panel_view: {self.view.inventory_panel_view}")
        logger.debug(f"  grid_view: {self.view.grid_view}")
        logger.debug(f"  item_panel_view: {self.view.item_panel_view}")

    def draw(self, screen):
        """
        Dibuja la vista del editor.
        """
        self.view.draw(screen, self.model, self.world)
