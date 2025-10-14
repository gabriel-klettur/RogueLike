import pygame
from typing import Any, Dict, Optional
from roguelike_ui.widgets.text_input.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector

from .items_properties_panel_models import ItemsPropertiesPanelModel
from .items_properties_panel_view import ItemsPropertiesPanelView
from .items_properties_panel_events import ItemsPropertiesPanelEventHandler
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import (
    EntitiesAssetsPickerPanelController,
)
from .services.schema import ensure_schema
from .services.item_data import get_item_data
from .services.editing import select_key_to_edit, get_initial_text
from .services.controller_ops import (
    commit_edit as ops_commit_edit,
    confirm_add_item_on_system as ops_confirm_add,
    on_asset_chosen as ops_on_asset_chosen,
)

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
        # Reutilizamos el picker de assets de entidades como un selector de archivos genérico
        self.assets_picker = EntitiesAssetsPickerPanelController()
        # Referencia inversa opcional al orquestador (ItemsEditorController)
        self.editor_controller = None

        # Datos externos
        self._items: Dict[str, Any] = items
        self._selected_id: Optional[str] = None
        self._hovered_id: Optional[str] = None
        # Callbacks inyectados por el orquestador (ItemsEditorController)
        # get_assets_anchor_rect() -> pygame.Rect | None
        self.get_assets_anchor_rect = None
        # on_asset_changed(item_id: str, new_asset_path: str) -> None
        self.on_asset_changed = None
        # on_after_commit_edit(key: str, old_id: str, new_id: Optional[str], value: Any) -> None
        # Se invoca tras persistir un cambio (incluyendo rename de 'id') para que el orquestador refresque caches.
        self.on_after_commit_edit = None

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
        # Dar prioridad al picker si está visible
        try:
            if self.assets_picker.handle_event(event):
                return
        except Exception:
            pass
        self.event_handler.handle(event)

    def draw(self, screen: pygame.Surface, title_rect: Optional[pygame.Rect] = None) -> None:
        # Cargar esquema al entrar en modo add-on-system sin selección
        try:
            if getattr(self.model, 'show_add_system_selector', False) and not (self._selected_id or self._hovered_id):
                ensure_schema(self.model)
        except Exception:
            pass
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
        # Dibujar el picker de assets si está visible
        try:
            self.assets_picker.draw(screen)
        except Exception:
            pass

    def start_inline_edit(self, prop_key: Optional[str] = None) -> None:
        """Inicia edición inline para la propiedad indicada o la primera disponible.

        Si no se provee prop_key, se selecciona la primera clave válida del ítem activo.
        Ahora se permiten todas las propiedades, incluyendo 'name', 'description' e 'id'.
        """
        active_id = self._selected_id or self._hovered_id
        if not active_id or active_id not in self._items:
            return
        item = self._items.get(active_id)
        if item is None:
            return
        data = get_item_data(item)
        key_to_edit: Optional[str] = select_key_to_edit(data, prop_key)
        if not key_to_edit:
            return
        self.model.focused_property = key_to_edit
        self.model.editing_property = key_to_edit
        initial = get_initial_text(item, key_to_edit)
        self.model.editing_text = initial
        self.model.editing_cursor = len(initial)
        self.text_input.activate(initial)

    # ---- Persistencia de cambios ----
    def commit_edit(self):
        ops_commit_edit(self)

    # ---- Confirmar "Add Item on System" ----
    def confirm_add_item_on_system(self) -> None:
        """Persiste el ítem actualmente seleccionado o el borrador y sale del modo 'add_item_on_system'."""
        ops_confirm_add(self)

    # ---- Esquema desde items.json ----
    # Método privado eliminado: el controlador usa ensure_schema() directamente en draw()

    # ---- Assets Picker ----
    def open_assets_picker(self) -> None:
        """Muestra el selector de imágenes anclado bajo el Items Picker si es posible; si no, bajo la celda."""
        cell = getattr(self.model, 'asset_cell_rect', None)
        anchor_rect = None
        try:
            if callable(self.get_assets_anchor_rect):
                anchor_rect = self.get_assets_anchor_rect()
        except Exception:
            anchor_rect = None
        if not cell and not anchor_rect:
            return
        # Ancho preferido: usar el ancho del ancla del picker si está disponible; si no, el viewport del panel
        if anchor_rect:
            width = max(180, anchor_rect.w)
            x = anchor_rect.x
            y = anchor_rect.bottom + 6
        else:
            width = max(180, (self.model.content_view_rect.w if self.model.content_view_rect else cell.w))
            x = cell.x
            y = cell.bottom + 6
        # Etiqueta inferior opcional: id del ítem activo
        def _label_provider() -> str:
            return (self._selected_id or self._hovered_id) or ""
        # Usamos la clave lógica 'icon'; el callback ajustará la clave real a actualizar
        self.assets_picker.show(key="icon", x=x, y=y, width=width, callback=self._on_asset_chosen, label_provider=_label_provider)
        # Tutorial pulse: assets picker opened
        try:
            editor = getattr(self, 'editor_controller', None)
            if editor is not None:
                setattr(editor.model, 'tutorial_assets_picker_open_pulse', True)
        except Exception:
            pass

    def _on_asset_chosen(self, cell_key: str, path) -> None:
        """Callback cuando el usuario elige una imagen en el picker.

        Conserva el tipo de dato existente (str o list) en el JSON del ítem.
        """
        ops_on_asset_chosen(self, cell_key, path)
