import os
import re
import pygame
from typing import Any, Dict, Optional
from pathlib import Path
import json

from roguelike_ui.widgets.text_input.text_input import TextInput
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
                self._ensure_schema_loaded()
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
        else:
            # No hay ítem activo: escribir en el borrador (modo add-on-system)
            key = self.model.editing_property
            new_text = self.model.editing_text
            # Intentar convertir según tipos del esquema si existen
            converted: Any = new_text
            try:
                schema_types = getattr(self.model, 'schema_types', {}) if hasattr(self.model, 'schema_types') else {}
                t = schema_types.get(key)
                if t is bool:
                    converted = new_text.lower() in ("true", "1", "yes")
                elif t is int:
                    converted = int(new_text)
                elif t is float:
                    converted = float(new_text)
                elif t is dict:
                    try:
                        converted = json.loads(new_text)
                        if not isinstance(converted, dict):
                            converted = {}
                    except Exception:
                        converted = {}
                elif t is list:
                    try:
                        converted = json.loads(new_text)
                        if not isinstance(converted, list):
                            converted = [str(new_text)] if new_text else []
                    except Exception:
                        converted = [str(new_text)] if new_text else []
                else:
                    converted = new_text
            except Exception:
                converted = new_text
            # Guardar en el borrador y cerrar edición
            self.model.new_item_draft[key] = converted
            self.text_input.deactivate()
            self.model.editing_property = None
            self.model.editing_text = ""
            self.model.editing_cursor = 0
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0

    # ---- Confirmar "Add Item on System" ----
    def confirm_add_item_on_system(self) -> None:
        """Persiste el ítem actualmente seleccionado o el borrador y sale del modo 'add_item_on_system'."""
        path = os.path.join(os.getcwd(), "data", "items", "items.json")
        item_id = self._selected_id or self._hovered_id
        entry = None
        if item_id:
            item = self._items.get(item_id)
            if item is None:
                return
            if hasattr(item, 'model_dump'):
                entry = item.model_dump()
            else:
                try:
                    entry = item.dict()
                except Exception:
                    entry = vars(item)
        else:
            # Usar borrador; requerir 'id'
            draft = dict(self.model.new_item_draft)
            new_id = str(draft.get('id', '')).strip()
            if not new_id:
                logger.error("[ItemsPropertiesPanel] Cannot confirm: missing 'id' in new item draft")
                return
            # Limpiar campos vacíos
            entry = {k: v for k, v in draft.items() if v not in (None, "", [], {})}
            entry['id'] = new_id
            item_id = new_id
        # Normalizar/validar mínimos para cumplir el esquema antes de guardar
        try:
            # Claves permitidas según schemas/items/common.json (propiedades de 'item')
            allowed_keys = {
                'id','name','description','stackable','max_stack','icon','icon_small','icon_large',
                'threshold','experience','effect','equip_slot','durability','damage','attack_speed',
                'range','crit_chance','crit_multiplier','weight','value','rarity','level_requirement',
                'quest_id','scale_editor','scale_map','scale_inventory','z_layer','default_params'
            }
            # Filtrar propiedades desconocidas (el esquema tiene additionalProperties=false)
            entry = {k: v for k, v in entry.items() if k in allowed_keys}
            # Eliminar valores vacíos/None para cualquier clave
            entry = {k: v for k, v in entry.items() if v not in (None, "", [], {})}
            # Validar patrón de id (schema: patternProperties ^[a-z0-9_]+$)
            if not re.fullmatch(r'^[a-z0-9_]+$', entry['id']):
                logger.error("[ItemsPropertiesPanel] Cannot confirm: id must match ^[a-z0-9_]+$ (lowercase, digits, underscore)")
                return
            # Defaults requeridos por el esquema
            if 'name' not in entry:
                entry['name'] = entry['id']
            if 'description' not in entry:
                entry['description'] = ""
            if 'stackable' not in entry:
                entry['stackable'] = False
            # Validar iconografía requerida (anyOf: icon OR icon_small+icon_large)
            has_icon = bool(entry.get('icon'))
            has_both_sizes = bool(entry.get('icon_small')) and bool(entry.get('icon_large'))
            if not (has_icon or has_both_sizes):
                logger.error("[ItemsPropertiesPanel] Cannot confirm: missing icon (need 'icon' or both 'icon_small' and 'icon_large')")
                return
            # Sanear max_stack de acuerdo al esquema (entero >=1)
            if 'max_stack' in entry:
                try:
                    if isinstance(entry['max_stack'], bool):
                        # Evitar que True/False pasen como 1/0
                        del entry['max_stack']
                    elif not isinstance(entry['max_stack'], int) or entry['max_stack'] < 1:
                        del entry['max_stack']
                except Exception:
                    entry.pop('max_stack', None)
            # Si es apilable y no se definió max_stack, establecer mínimo válido
            if entry.get('stackable') is True and 'max_stack' not in entry:
                entry['max_stack'] = 1
            # Sanear default_params según esquema
            if 'default_params' in entry and isinstance(entry['default_params'], dict):
                allowed_params = {
                    'dest_map','dest_x','dest_y','healing','mana','energy','hunger',
                    'buff_stat','buff_value','duration','key_id','event_id'
                }
                entry['default_params'] = {k: v for k, v in entry['default_params'].items() if k in allowed_params}
        except Exception:
            # Si algo falla aquí, preferimos no guardar para no romper el loader
            logger.exception("[ItemsPropertiesPanel] Validation/normalization failed before save")
            return
        # Guardar JSON completo para este ítem
        try:
            save_to_json(path, item_id, entry)
        except Exception:
            logger.exception("[ItemsPropertiesPanel] Failed to save item entry on confirm")
        # Salir de add-on-system, limpiar panel y mostrar picker, luego refrescar catálogo
        try:
            # Cerrar selector de assets si estaba abierto
            self.model.show_add_system_selector = False
            # Limpiar borrador y estado de edición
            try:
                self.model.new_item_draft.clear()
            except Exception:
                pass
            self.model.editing_property = None
            self.model.editing_text = ""
            self.model.editing_cursor = 0
            # Limpiar selección interna
            self._selected_id = None
            self._hovered_id = None
            if self.editor_controller is not None:
                # Desactivar el botón activo en la sub-toolbar
                try:
                    arm = getattr(self.editor_controller, 'items_add_remove_model', None)
                    if arm and getattr(arm, 'active_tool', None) == 'add_item_on_system':
                        arm.active_tool = None
                except Exception:
                    pass
                # Restaurar layout y mostrar el picker nuevamente
                try:
                    if hasattr(self.editor_controller, 'exit_add_items_on_system_mode'):
                        self.editor_controller.exit_add_items_on_system_mode()
                except Exception:
                    pass
                # Asegurar visibilidad del picker explícitamente
                try:
                    self.editor_controller.picker_controller.model.visible = True
                except Exception:
                    pass
                # Refrescar catálogos/caches para habilitar uso inmediato
                try:
                    self.editor_controller._refresh_items_catalog()
                except Exception:
                    logger.exception("[ItemsPropertiesPanel] Failed to refresh items catalog after confirm")
                # Tutorial pulse: add system confirm
                try:
                    setattr(self.editor_controller.model, 'tutorial_add_system_confirm_pulse', True)
                except Exception:
                    pass
        except Exception:
            pass

    # ---- Esquema desde items.json ----
    def _ensure_schema_loaded(self) -> None:
        if getattr(self.model, 'schema_keys', None):
            return
        try:
            path = os.path.join(os.getcwd(), "data", "items", "items.json")
            data = load_from_json(path)
        except Exception:
            data = {}
        keys_set = set()
        type_map: dict[str, type] = {}
        for _, entry in (data or {}).items():
            if not isinstance(entry, dict):
                continue
            for k, v in entry.items():
                keys_set.add(k)
                if v is None:
                    continue
                t = type(v)
                # Prefer stable basic types if mixed
                prev = type_map.get(k)
                if prev is None or prev is str and t is not str:
                    type_map[k] = t
        # Orden sugerido
        preferred = ["id", "name", "description", "icon", "icon_small", "icon_large"]
        ordered = [k for k in preferred if k in keys_set]
        for k in sorted(keys_set):
            if k not in ordered:
                ordered.append(k)
        self.model.schema_keys = ordered
        # Guardar tipos simplificados (usar clases: bool,int,float,str,dict,list)
        simple = {}
        for k, t in type_map.items():
            if t in (bool, int, float, str, dict, list):
                simple[k] = t
            else:
                simple[k] = str
        # Adjuntar al modelo de forma perezosa
        setattr(self.model, 'schema_types', simple)

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
        try:
            item_id = self._selected_id or self._hovered_id
            if not item_id or item_id not in self._items:
                # No hay ítem: escribir en borrador
                target_key = 'icon'
                for k in ('icon', 'icon_small', 'icon_large'):
                    if hasattr(self.model, 'schema_keys') and k in getattr(self.model, 'schema_keys', []):
                        target_key = k
                        break
                try:
                    rel = Path(path).resolve().relative_to(Path(ASSETS_DIR).resolve()).as_posix()
                    asset_value = f"assets/{rel}"
                except Exception:
                    asset_value = str(path)
                old_val = self.model.new_item_draft.get(target_key)
                if isinstance(old_val, list):
                    if len(old_val) > 0:
                        old_val[0] = asset_value
                    else:
                        self.model.new_item_draft[target_key] = [asset_value]
                else:
                    self.model.new_item_draft[target_key] = asset_value
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
            # Tutorial pulse: asset changed
            try:
                editor = getattr(self, 'editor_controller', None)
                if editor is not None:
                    setattr(editor.model, 'tutorial_asset_changed_pulse', True)
            except Exception:
                pass
        finally:
            # Ocultar picker en cualquier caso
            try:
                self.assets_picker.hide()
            except Exception:
                pass
