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

    def update_context(self, items: Dict[str, Any], selected_id: Optional[str], hovered_id: Optional[str]):
        """Actualiza en un solo paso los ítems y los ids activos.

        Esto reduce llamadas duplicadas desde el Picker.
        """
        self._items = items
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

    def start_inline_edit(self, prop_key: Optional[str] = None) -> None:
        """Inicia edición inline para la propiedad indicada o la primera disponible.

        Si no se provee prop_key, se selecciona la primera clave válida del ítem activo
        (excluyendo 'name' y 'description' y valores None) para que coincida
        con lo que realmente se muestra en la vista.
        """
        active_id = self._selected_id or self._hovered_id
        if not active_id or active_id not in self._items:
            return
        item = self._items.get(active_id)
        if item is None:
            return
        # Obtener datos similares a la vista
        if hasattr(item, 'model_dump'):
            data = item.model_dump()
        else:
            try:
                data = item.dict()
            except Exception:
                data = vars(item)
        # Determinar propiedad destino
        key_to_edit: Optional[str] = prop_key
        if key_to_edit is None:
            for k, v in data.items():
                if k in ("name", "description") or v is None:
                    continue
                key_to_edit = k
                break
        if not key_to_edit:
            return
        # Configurar modelo y TextInput
        self.model.focused_property = key_to_edit
        self.model.editing_property = key_to_edit
        initial = str(getattr(item, key_to_edit, ""))
        self.model.editing_text = initial
        self.model.editing_cursor = len(initial)
        self.text_input.activate(initial)

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
