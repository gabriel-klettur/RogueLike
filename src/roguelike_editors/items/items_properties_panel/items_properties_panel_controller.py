import os
import pygame
from typing import Any, Dict, Optional

from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.services.json_persistence import save_to_json, load_from_json

from .items_properties_panel_models import ItemsPropertiesPanelModel
from .items_properties_panel_view import ItemsPropertiesPanelView
from .items_properties_panel_events import ItemsPropertiesPanelEventHandler

import logging
logger = logging.getLogger(__name__)


class ItemsPropertiesPanelController:
    """Controller para el panel de propiedades de ítems."""

    def __init__(self, items: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemsPropertiesPanelModel()
        self.view = ItemsPropertiesPanelView(font)
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.event_handler = ItemsPropertiesPanelEventHandler(self)

        # Datos externos
        self._items: Dict[str, Any] = items
        self._selected_id: Optional[str] = None
        self._hovered_id: Optional[str] = None

    # ---- Enlaces externos ----
    def set_items(self, items: Dict[str, Any]):
        self._items = items

    def set_active_ids(self, selected_id: Optional[str], hovered_id: Optional[str]):
        self._selected_id = selected_id
        self._hovered_id = hovered_id

    # ---- Bucle de UI ----
    def handle_event(self, event: pygame.event.Event) -> None:
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface, title_rect: Optional[pygame.Rect] = None) -> None:
        active_id = self._selected_id or self._hovered_id
        self.view.draw(screen, self.model, self._items, active_id, title_rect)
        # Overlay de edición inline (caret)
        if self.model.editing_property and self.text_input.active:
            for rect_prop, key_prop in self.model.property_entries:
                if key_prop == self.model.editing_property:
                    prefix = f"{key_prop}: "
                    x = rect_prop.x + self.view.font.size(prefix)[0]
                    y = rect_prop.y
                    self.text_input.draw(screen, x, y)
                    break

    # ---- Persistencia de cambios ----
    def commit_edit(self):
        if not self.model.editing_property:
            return
        item_id = self._selected_id or self._hovered_id
        if item_id and item_id in self._items:
            item = self._items[item_id]
            key = self.model.editing_property
            new_text = self.model.editing_text
            old_val = getattr(item, key, None)
            try:
                if isinstance(old_val, bool):
                    converted = new_text.lower() in ("true", "1", "yes")
                elif isinstance(old_val, int):
                    converted = int(new_text)
                elif isinstance(old_val, float):
                    converted = float(new_text)
                else:
                    converted = new_text
            except ValueError:
                converted = new_text
            try:
                setattr(item, key, converted)
            except Exception as e:
                logger.error(f"[ItemsPropertiesPanel] Invalid assignment for {key}: '{converted}', error: {e}")
                # cleanup on invalid input
                self.text_input.deactivate()
                self.model.editing_property = None
                self.model.editing_text = ""
                self.model.editing_cursor = 0
                return
            # Guardar JSON
            path = os.path.join(os.getcwd(), "data", "items", "items.json")
            data = load_from_json(path)
            entry = data.get(item_id, {})
            entry[key] = converted
            save_to_json(path, item_id, entry)
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
