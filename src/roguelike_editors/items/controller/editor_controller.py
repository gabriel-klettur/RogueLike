import pygame
# Initialize font module to ensure SysFont works in tests
pygame.font.init()
# Wrap SysFont to ensure font module is initialized on each call
_orig_sysfont = pygame.font.SysFont

def _safe_sysfont(*args, **kwargs):
    pygame.font.init()
    return _orig_sysfont(*args, **kwargs)

pygame.font.SysFont = _safe_sysfont
from typing import Any, Dict
from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_editors.items.view.editor_view import ItemEditorView

from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_ui.widgets.map_items_ui import MapItemsUI
from roguelike_ui.widgets.params_editor_ui import ParamsEditorUI
from roguelike_ui.services.json_persistence import save_to_json
import os
from roguelike_editors.items.events.items_editor_events import ItemsEditorEventHandler

class ItemEditorController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemEditorModel(items=items, assets=assets)
        self.view = ItemEditorView(assets, font)

        # Initialize text input and double-click detector
        self.text_input = TextInput(font)
        self.dc_detector = DoubleClickDetector()
        self.view.text_input = self.text_input
        # Inicializar servicios de edición de instancias
        # Map items list
        inv_map_path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
        self.map_ui = MapItemsUI(font, inv_map_path)
        # Params editor
        schema_path = os.path.join(os.getcwd(), 'schemas', 'items', 'instances.json')
        self.params_ui = ParamsEditorUI(schema_path, font)
        # Enlazar al view
        self.view.map_ui = self.map_ui
        self.view.params_ui = self.params_ui
        # Handler de eventos inline y grid
        self.event_handler = ItemsEditorEventHandler(self)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Delegar entrada inline (grid, detalles, edición) al handler existente
        self.event_handler.handle(event)
        # Integración de lista de instancias del mapa y edición de params
        if self.model.visible:
            # Selección de instancia en mapa
            inst = self.map_ui.handle_event(event)
            if inst:
                inst_data = self.map_ui.data.get(inst, {})
                # Seleccionar ítem en el grid de definiciones
                item_def = inst_data.get('item_id')
                if item_def:
                    self.model.selected_item_id = item_def
                # cargar valores al editor de params
                params = inst_data.get('params', {})
                self.params_ui.load_values(params)
                return
            # Manejo de edición de params
            if self.params_ui.handle_event(event):
                try:
                    new_params = self.params_ui.get_values()
                    inst_id = self.map_ui.selected_instance
                    if inst_id:
                        # actualizar datos en memoria y persistir
                        entry = self.map_ui.data.get(inst_id, {})
                        entry['params'] = new_params
                        path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
                        save_to_json(path, inst_id, entry)
                        # refrescar lista de mapa
                        self.map_ui.load()
                    return
                except ValidationError as e:
                    print(f"Params invalidos: {e}")
                    return
        self.event_handler.handle(event)
        return
        if self.model.visible:
            # Definiciones de ítems
            if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
                idx = self.def_panel.get_selected(event.pos)
                if idx is not None:
                    def_id = self.def_panel.items[idx]
                    self.model.selected_item_id = def_id
                    # reset instancia y params
                    self.map_ui.selected_instance = None
                    return
            # Instancias en el mapa
            inst = self.map_ui.handle_event(event)
            if inst:
                self.model.selected_instance_id = inst
                params = self.map_ui.data.get(inst, {}).get('params', {})
                # cargar valores en editor de params
                self.params_ui.load_values(params)
                return
            # Edición de params
            if self.params_ui.handle_event(event):
                try:
                    new_params = self.params_ui.get_values()
                    inst_id = self.map_ui.selected_instance
                    if inst_id:
                        entry = self.map_ui.data.get(inst_id, {})
                        entry['params'] = new_params
                        # Guardar en JSON
                        import os
                        path = os.path.join(os.getcwd(), 'data', 'inventory', 'inventory_map.json')
                        save_to_json(path, inst_id, entry)
                    return
                except ValidationError as e:
                    print(f"Params invalidos: {e}")
                    return
        # flujo existente...
        self.event_handler.handle(event)
        return
        # Inline editing input

        if self.text_input.active:
            if self.text_input.handle_event(event):
                # sync model text and cursor for view
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                # on Enter, TextInput deactivated and commit
                if not self.text_input.active:
                    self._commit_edit()
                return
            # swallow other events when editing
            return

        if False and self.model.visible and self.model.editing_property:
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_RETURN:
                    self._commit_edit()
                    return
                elif event.key == pygame.K_BACKSPACE:
                    if self.model.editing_cursor > 0:
                        # delete char before cursor
                        self.model.editing_text = (self.model.editing_text[:self.model.editing_cursor-1] + self.model.editing_text[self.model.editing_cursor:])
                        self.model.editing_cursor -= 1
                    return
                elif event.key == pygame.K_LEFT:
                    # move cursor left
                    self.model.editing_cursor = max(0, self.model.editing_cursor-1)
                    return
                elif event.key == pygame.K_RIGHT:
                    # move cursor right
                    self.model.editing_cursor = min(len(self.model.editing_text), self.model.editing_cursor+1)
                    return
                else:
                    # insert character at cursor
                    ch = event.unicode
                    if ch:
                        et = self.model.editing_text
                        idx = self.model.editing_cursor
                        self.model.editing_text = et[:idx] + ch + et[idx:]
                        self.model.editing_cursor += len(ch)
                    return
            elif event.type == pygame.MOUSEBUTTONDOWN:
                mx, my = event.pos
                # Only commit when clicking outside editing property
                if hasattr(self.model, 'property_entries'):
                    for rect_prop, key_prop in self.model.property_entries:
                        if key_prop == self.model.editing_property and rect_prop.collidepoint(mx, my):
                            return
                self._commit_edit()
                return
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F7:
                self.model.visible = not self.model.visible
                print(f"[DEBUG ItemEditorController] F7 pressed, visible={self.model.visible}")
                if not self.model.visible:
                    self.model.selected_item_id = None
            elif self.model.visible:
                if event.key == pygame.K_UP:
                    self.model.scroll_index = max(0, self.model.scroll_index - 1)
                elif event.key == pygame.K_DOWN:
                    self.model.scroll_index = min(len(self.model.items) - 1, self.model.scroll_index + 1)

        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            entries = [k for (_r, k) in getattr(self.model, 'property_entries', [])]
            print(f"[DEBUG controller] MOUSEBUTTONDOWN clicks={getattr(event, 'clicks',1)} pos=({mx},{my}) entries={entries}")
            
            # Click on property: focus or edit
            if hasattr(self.model, 'property_entries'):
                for rect, key in self.model.property_entries:
                    if rect.collidepoint(mx, my):
                        # Double-click detection
                        if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                            self.model.focused_property = key
                            self.model.editing_property = key
                            # prefill and activate text input
                            item_id = self.model.selected_item_id or self.model.hovered_item_id
                            item = self.model.items.get(item_id)
                            initial = str(getattr(item, key, "")) if item else ""
                            # preserve the existing value and cursor
                            self.model.editing_text = initial
                            self.model.editing_cursor = len(initial)
                            self.text_input.activate(initial)  # caret at end by default
                        else:
                            self.model.focused_property = key
                        return
            # Si clic en panel de detalles, conservar foco/edición
            if hasattr(self.model, 'panel_rect') and self.model.panel_rect.collidepoint(mx, my):
                return
            # Clic en grilla de ítems: calcular pantalla y columnas
            screen_surf = pygame.display.get_surface()
            if screen_surf:
                sw, sh = screen_surf.get_size()
            else:
                sw, sh = None, None
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            # Columnas fijas: 12
            columns = 12
            # Seleccionar ítem en grilla
            if mx < margin or my < margin:
                self.model.selected_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                # Filtrar ítems excluyendo placeholder
                item_ids = [i for i in self.model.items.keys() if i != "image_item_not_found"]
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_item_id = item_ids[idx]
                else:
                    self.model.selected_item_id = None
            # Limpiar foco y modo edición al cambiar selección
            self.model.focused_property = None
            self.model.editing_property = None
        elif event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            screen_surf = pygame.display.get_surface()
            if screen_surf:
                sw, sh = screen_surf.get_size()
            else:
                sw, sh = None, None
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            # Columnas fijas: 12
            columns = 12
            # Verificar área vertical
            if mx < margin or my < margin:
                self.model.hovered_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                # Filtrar ítems excluyendo placeholder
                item_ids = [i for i in self.model.items.keys() if i != "image_item_not_found"]
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_item_id = item_ids[idx]
                else:
                    self.model.hovered_item_id = None

        else:
            # Reset hover cuando otros eventos
            self.model.hovered_item_id = None

    def _commit_edit(self):
        if not self.model.editing_property:
            return
        item_id = self.model.selected_item_id or self.model.hovered_item_id
        if item_id and item_id in self.model.items:
            item = self.model.items[item_id]
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
                print(f"[ItemEditor] Invalid assignment for {key}: '{converted}', error: {e}")
                # cleanup on invalid input
                self.text_input.deactivate()
                self.model.editing_property = None
                self.model.editing_text = ""
                self.model.editing_cursor = 0
                return
            # Guardar JSON
            from roguelike_ui.services.json_persistence import load_from_json
            path = os.path.join(os.getcwd(), "data", "items.json")
            data = load_from_json(path)
            entry = data.get(item_id, {})
            entry[key] = converted
            save_to_json(path, item_id, entry)
        self.model.editing_property = None
        self.model.editing_text = ""
        self.model.editing_cursor = 0
    def draw(self, screen: pygame.Surface) -> None:
        # Mostrar editor de ítems original
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
        # Overlay de edición inline
        if self.model.editing_property:
            for rect_prop, key_prop in self.model.property_entries:
                if key_prop == self.model.editing_property:
                    prefix = f"{key_prop}: "
                    x = rect_prop.x + self.view.font.size(prefix)[0]
                    y = rect_prop.y
                    self.text_input.draw(screen, x, y)
                    break
        # Añadir lista de instancias del mapa y editor de params debajo
        margin = 20
        sw, sh = screen.get_size()
        # Panel de params en la parte inferior
        params_h = sh // 4
        list_h = sh // 4
        params_rect = pygame.Rect(margin, sh - margin - params_h, sw - 2*margin, params_h)
        # Dibujar params si hay instancia seleccionada
        if getattr(self.map_ui, 'selected_instance', None):
            self.params_ui.draw(screen, params_rect)
        # Panel de lista de instancias justo encima
        list_rect = pygame.Rect(margin, params_rect.y - margin - list_h, sw - 2*margin, list_h)
        self.map_ui.draw(screen, list_rect)
