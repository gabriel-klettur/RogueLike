import os
import pygame
from typing import Any, Dict, Optional
from pathlib import Path
import json

from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.services.json_persistence import save_to_json, load_from_json

from .items_properties_panel_models import ItemsPropertiesPanelModel
from .items_properties_panel_view import ItemsPropertiesPanelView
from .items_properties_panel_events import ItemsPropertiesPanelEventHandler
from roguelike_editors.entities.entities_assets_picker_panel.entities_assets_picker_panel_controller import (
    EntitiesAssetsPickerPanelController,
)
from roguelike_engine.config.config import ASSETS_DIR

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
            # Priorizar 'name' si existe, luego 'description', luego la primera disponible
            for candidate in ("name", "description"):
                if candidate in data:
                    key_to_edit = candidate
                    break
            if key_to_edit is None:
                for k, v in data.items():
                    if v is None:
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
            # Guardar JSON (con manejo especial para 'id')
            path = os.path.join(os.getcwd(), "data", "items", "items.json")
            data_json = load_from_json(path)
            entry = data_json.get(item_id, {})

            if key == 'id':
                new_id = str(converted)
                if not new_id:
                    logger.warning("[ItemsPropertiesPanel] Empty id not allowed; ignoring change")
                elif new_id == item_id:
                    # Nada que renombrar; solo asegurar atributo y JSON consistente
                    try:
                        setattr(item, 'id', new_id)
                    except Exception:
                        pass
                    entry['id'] = new_id
                    save_to_json(path, item_id, entry)
                    # Notificar edición (sin cambio real de id)
                    try:
                        if callable(self.on_after_commit_edit):
                            self.on_after_commit_edit('id', item_id, item_id, new_id)
                    except Exception:
                        logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
                else:
                    if new_id in data_json:
                        # Colisión: revertir y abortar
                        logger.error(f"[ItemsPropertiesPanel] Cannot rename id: '{new_id}' already exists")
                        try:
                            setattr(item, 'id', item_id)
                        except Exception:
                            pass
                    else:
                        # Actualizar objeto en memoria
                        try:
                            setattr(item, 'id', new_id)
                        except Exception:
                            pass
                        # Mover entrada en el JSON y reescribir archivo completo
                        entry['id'] = new_id
                        data_json[new_id] = entry
                        if item_id in data_json:
                            del data_json[item_id]
                        try:
                            with open(path, 'w', encoding='utf-8') as f:
                                json.dump(data_json, f, ensure_ascii=False, indent=2)
                        except Exception as e:
                            logger.exception(f"[ItemsPropertiesPanel] Failed to rewrite items JSON on id rename: {e}")
                        # Actualizar mapa en memoria y selección
                        try:
                            self._items[new_id] = self._items.pop(item_id)
                        except Exception:
                            pass
                        if self._selected_id == item_id:
                            self._selected_id = new_id
                        if self._hovered_id == item_id:
                            self._hovered_id = new_id
                        # Notificar cambio de id para permitir refresh de caches
                        try:
                            if callable(self.on_after_commit_edit):
                                self.on_after_commit_edit('id', item_id, new_id, new_id)
                        except Exception:
                            logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
            else:
                # Asignación normal del atributo
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
                # Persistencia del campo actualizado
                entry[key] = converted
                save_to_json(path, item_id, entry)
                # Notificar cambio normal de propiedad
                try:
                    if callable(self.on_after_commit_edit):
                        self.on_after_commit_edit(key, item_id, None, converted)
                except Exception:
                    logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed")
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0

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

    def _on_asset_chosen(self, cell_key: str, path) -> None:
        """Callback cuando el usuario elige una imagen en el picker.

        Conserva el tipo de dato existente (str o list) en el JSON del ítem.
        """
        try:
            item_id = self._selected_id or self._hovered_id
            if not item_id or item_id not in self._items:
                return
            item = self._items[item_id]
            # Determinar la clave a actualizar
            if hasattr(item, 'model_dump'):
                data = item.model_dump()
            else:
                try:
                    data = item.dict()
                except Exception:
                    data = vars(item)
            target_key = 'icon'
            for k in ('icon', 'icon_small', 'icon_large'):
                if k in data:
                    target_key = k
                    break
            # Normalizar ruta relativa a assets/
            try:
                rel = Path(path).resolve().relative_to(Path(ASSETS_DIR).resolve()).as_posix()
                asset_value = f"assets/{rel}"
            except Exception:
                # Si falla, usar tal cual
                asset_value = str(path)

            # Actualizar en memoria respetando tipo anterior
            old_val = getattr(item, target_key, None)
            if isinstance(old_val, list):
                if len(old_val) > 0:
                    old_val[0] = asset_value
                else:
                    setattr(item, target_key, [asset_value])
            else:
                try:
                    setattr(item, target_key, asset_value)
                except Exception:
                    # Si no existe el atributo aún
                    try:
                        setattr(item, target_key, asset_value)
                    except Exception:
                        pass

            # Persistir a JSON
            path_json = os.path.join(os.getcwd(), "data", "items", "items.json")
            data_json = load_from_json(path_json)
            entry = data_json.get(item_id, {})
            if isinstance(entry.get(target_key), list):
                if len(entry[target_key]) > 0:
                    entry[target_key][0] = asset_value
                else:
                    entry[target_key] = [asset_value]
            else:
                entry[target_key] = asset_value
            save_to_json(path_json, item_id, entry)
            # Notificar al orquestador para refrescar icono en el picker inmediatamente
            try:
                if callable(self.on_asset_changed):
                    self.on_asset_changed(item_id, asset_value)
            except Exception:
                logger.exception("[ItemsPropertiesPanel] on_asset_changed callback failed")
            # Notificar post-commit para refrescar catálogos (items/assets) globales y caches ECS
            try:
                if callable(self.on_after_commit_edit):
                    # Indicamos la clave específica actualizada (p.ej. 'icon'/'icon_small')
                    self.on_after_commit_edit(target_key, item_id, None, asset_value)
            except Exception:
                logger.exception("[ItemsPropertiesPanel] on_after_commit_edit callback failed tras cambio de asset")
        finally:
            # Ocultar picker en cualquier caso
            try:
                self.assets_picker.hide()
            except Exception:
                pass
