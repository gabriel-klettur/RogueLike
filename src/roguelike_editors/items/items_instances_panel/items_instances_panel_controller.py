import os
import pygame
import logging
from typing import Any, Optional, Callable

from .items_instances_panel_model import ItemsInstancesPanelModel
from .items_instances_panel_view import ItemsInstancesPanelView
from .items_instances_panel_events import ItemsInstancesPanelEvents

from roguelike_ui.widgets.map_items_ui import MapItemsUI
from roguelike_ui.widgets.params_editor_ui import ParamsEditorUI


class ItemsInstancesPanelController:
    """
    Controlador del panel inferior de instancias del mapa y editor de parámetros.
    Se encarga de crear y orquestar MapItemsUI y ParamsEditorUI.
    """
    def __init__(self, font: pygame.font.Font):
        self.model = ItemsInstancesPanelModel()
        self.view = ItemsInstancesPanelView()
        self.events = ItemsInstancesPanelEvents()

        # Crear UIs
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'active', 'inventory_map.json')
        self.map_ui = MapItemsUI(font, inv_map_path)
        schema_path = os.path.join(os.getcwd(), 'schemas', 'items', 'instances.json')
        self.params_ui = ParamsEditorUI(schema_path, font)

        # Callback para notificar selección de item por id (hacia el orquestador)
        self.on_select_item_id: Optional[Callable[[str], None]] = None

    # --- API pública ---
    def reload_data(self) -> None:
        """Recarga la lista de instancias desde disco."""
        logging.getLogger(__name__).debug("[ItemsInstancesPanelController.reload_data] reloading instances map json")
        self.map_ui.load()

    def handle_event(self, event: pygame.event.Event) -> bool:
        if not self.model.visible:
            return False
        return self.events.handle_event(self, event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        logging.getLogger(__name__).debug("[ItemsInstancesPanelController.draw] drawing instances panel")
        self.view.draw(screen, self.model, self.map_ui, self.params_ui)

    # --- Layout helpers ---
    def get_layout_rects(self) -> tuple[pygame.Rect, pygame.Rect]:
        """
        Calcula y retorna (list_rect, params_rect) para hit-testing.
        Usa la superficie de display actual si es posible.
        """
        screen = pygame.display.get_surface()
        if screen is None:
            # Fallback: usar una superficie dummy con tamaño 0 para evitar crashes
            screen = pygame.Surface((0, 0))
        self.view.layout(screen, self.model)
        return self.model.list_rect, self.model.params_rect
