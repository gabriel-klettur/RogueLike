import pygame
from typing import Any, Dict, Optional

from .items_editor_models import ItemsEditorModel
from .items_editor_events import ItemsEditorEvents
from .items_editor_view import ItemsEditorView

from .items_picker_panel.items_picker_panel_controller import ItemPickerPanelController
from .items_properties_panel.items_properties_panel_controller import ItemsPropertiesPanelController


class ItemsEditorController:
    """Orquestador del Editor de Ítems.

    Compone el PickerPanel y el PropertiesPanel y coordina su estado y eventos.
    """

    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        # Modelo global
        self.model = ItemsEditorModel(items=items, assets=assets)
        # Subcontroladores
        self.picker_controller = ItemPickerPanelController(items, assets, font)
        self.properties_controller = ItemsPropertiesPanelController(items, font)
        # Eventos/Vista del editor
        self.events = ItemsEditorEvents()
        self.view = ItemsEditorView()

        # Callbacks desde el picker hacia el editor
        def _on_select_id(item_id: str) -> None:
            self.model.selected_item_id = item_id
            # Sincronizar inmediatamente el panel de propiedades para mostrar el ítem seleccionado
            self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)

        def _on_open_id(item_id: str) -> None:
            # Seleccionar y abrir edición inline de la primera propiedad
            self.model.selected_item_id = item_id
            self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
            self.properties_controller.start_inline_edit()

        self.picker_controller.on_select_id = _on_select_id
        self.picker_controller.on_open_id = _on_open_id

        # Visibilidad inicial sincronizada
        self.picker_controller.model.visible = self.model.visible

    # --- Ciclo principal ---
    def handle_event(self, event: pygame.event.Event) -> None:
        if not self.model.visible:
            return
        # Delegación centralizada
        self.events.handle_event(self, event)

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        # Primero, dibujar picker (incluye overlay y título)
        self.picker_controller.draw(screen)
        # Sincronizar hover tras render del picker
        self.model.hovered_item_id = self.picker_controller.model.hovered_item_id
        # Luego, dibujar propiedades con el rect del título del picker
        title_rect: Optional[pygame.Rect] = getattr(self.picker_controller.view, 'title_rect', None)
        self.properties_controller.update_context(self.model.items, self.model.selected_item_id, self.model.hovered_item_id)
        self.properties_controller.draw(screen, title_rect)

    # --- Visibilidad ---
    def show(self) -> None:
        self.model.visible = True
        self.picker_controller.model.visible = True

    def hide(self) -> None:
        self.model.visible = False
        self.picker_controller.model.visible = False

    def toggle(self) -> None:
        if self.model.visible:
            self.hide()
        else:
            self.show()

    # --- Integraciones auxiliares ---
    def set_game(self, game: Any) -> None:
        """Permite a features del picker (RMB spawn) acceder al juego."""
        self.picker_controller.game = game

