import pygame
from typing import Any, Dict
from roguelike_editors.items.model.editor_model import ItemEditorModel
from roguelike_editors.items.view.editor_view import ItemEditorView

class ItemEditorController:
    """Controller para editor de ítems: maneja visibilidad y navegación."""
    def __init__(self, items: Dict[str, Any], assets: Dict[str, Any], font: pygame.font.Font):
        self.model = ItemEditorModel(items=items, assets=assets)
        self.view = ItemEditorView(assets, font)
        # Para detección manual de doble click en propiedades
        self.last_click_time = 0
        self.last_click_key = None
        self.double_click_interval = 500  # ms
        pygame.key.set_repeat(300, 50)  # enable key repeat for continuous backspace and arrow keys

    def handle_event(self, event: pygame.event.Event) -> None:
        # Inline editing input
        if self.model.visible and self.model.editing_property:
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
                if not self.model.visible:
                    self.model.selected_item_id = None
            elif self.model.visible:
                if event.key == pygame.K_UP:
                    self.model.scroll_index = max(0, self.model.scroll_index - 1)
                elif event.key == pygame.K_DOWN:
                    self.model.scroll_index = min(len(self.model.items) - 1, self.model.scroll_index + 1)

        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            print(f"[DEBUG controller] MOUSEBUTTONDOWN clicks={getattr(event, 'clicks',1)} pos=({mx},{my}) entries={[k for (_r,k) in self.model.property_entries]}")
            
            # Single-click on property: focus or start editing
            if hasattr(self.model, 'property_entries'):
                for rect, key in self.model.property_entries:
                    if rect.collidepoint(mx, my):
                        # Si ya había foco en esta propiedad, iniciar edición
                        if self.model.focused_property == key:
                            self.model.editing_property = key
                            # Prefill editing_text con valor actual
                            item_id = self.model.selected_item_id or self.model.hovered_item_id
                            item = self.model.items.get(item_id)
                            if item:
                                val = getattr(item, key, "")
                                self.model.editing_text = str(val)
                                self.model.editing_cursor = len(self.model.editing_text)  # start cursor at end
                            print(f"[DEBUG controller] editing_property={self.model.editing_property}, editing_text='{self.model.editing_text}'")
                        else:
                            self.model.focused_property = key
                            print(f"[DEBUG controller] focused_property set to {self.model.focused_property}")
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
            if sw:
                columns = max(1, (sw - margin) // (cell_size + margin))
            else:
                columns = 6
            # Seleccionar ítem en grilla
            if mx < margin or my < margin:
                self.model.selected_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = list(self.model.items.keys())
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
            if sw:
                columns = max(1, (sw - margin) // (cell_size + margin))
            else:
                columns = 6
            # Verificar área vertical
            if mx < margin or my < margin:
                self.model.hovered_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = list(self.model.items.keys())
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
            setattr(item, key, converted)
            # Guardar JSON
            import json, os
            path = os.path.join(os.getcwd(), "data", "items.json")
            try:
                with open(path, encoding="utf-8") as f:
                    data = json.load(f)
                data[item_id][key] = converted
                with open(path, "w", encoding="utf-8") as f:
                    json.dump(data, f, ensure_ascii=False, indent=2)
            except Exception as e:
                print(f"[ItemEditor] Error saving item {item_id}: {e}")
        self.model.editing_property = None
        self.model.editing_text = ""
    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        self.view.draw(screen, self.model)
