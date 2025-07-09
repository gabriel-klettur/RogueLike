import pygame
import os
from roguelike_ui.services.json_persistence import save_to_json

class ItemsEditorEventHandler:
    """
    Manejador de eventos para el editor de ítems.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.text_input = controller.text_input
        self.dc_detector = controller.dc_detector

    def handle(self, event: pygame.event.Event) -> None:
        # Inline editing input
        if self.text_input.active:
            if self.text_input.handle_event(event):
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                if not self.text_input.active:
                    self.controller._commit_edit()
                return
            return

        # Edición de propiedades (bloque inactivo)
        if False and self.model.visible and self.model.editing_property:
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_RETURN:
                    self.controller._commit_edit()
                    return
                elif event.key == pygame.K_BACKSPACE:
                    if self.model.editing_cursor > 0:
                        self.model.editing_text = (self.model.editing_text[:self.model.editing_cursor-1] + self.model.editing_text[self.model.editing_cursor:])
                        self.model.editing_cursor -= 1
                    return
                elif event.key == pygame.K_LEFT:
                    self.model.editing_cursor = max(0, self.model.editing_cursor-1)
                    return
                elif event.key == pygame.K_RIGHT:
                    self.model.editing_cursor = min(len(self.model.editing_text), self.model.editing_cursor+1)
                    return
                else:
                    ch = event.unicode
                    if ch:
                        et = self.model.editing_text
                        idx = self.model.editing_cursor
                        self.model.editing_text = et[:idx] + ch + et[idx:]
                        self.model.editing_cursor += len(ch)
                    return
            elif event.type == pygame.MOUSEBUTTONDOWN:
                mx, my = event.pos
                if hasattr(self.model, 'property_entries'):
                    for rect_prop, key_prop in self.model.property_entries:
                        if key_prop == self.model.editing_property and rect_prop.collidepoint(mx, my):
                            return
                self.controller._commit_edit()
                return

        # Teclas de toggle y navegación
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

        # Clicks del ratón
        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            entries = [k for (_r, k) in getattr(self.model, 'property_entries', [])]
            print(f"[DEBUG controller] MOUSEBUTTONDOWN clicks={getattr(event, 'clicks',1)} pos=({mx},{my}) entries={entries}")

            # Clic en propiedad: focus o edición
            if hasattr(self.model, 'property_entries'):
                for rect, key in self.model.property_entries:
                    if rect.collidepoint(mx, my):
                        if getattr(event, 'clicks',1) >= 2 or self.dc_detector.is_double_click(key):
                            self.model.focused_property = key
                            self.model.editing_property = key
                            item_id = self.model.selected_item_id or self.model.hovered_item_id
                            item = self.model.items.get(item_id)
                            initial = str(getattr(item, key, "")) if item else ""
                            self.model.editing_text = initial
                            self.model.editing_cursor = len(initial)
                            self.text_input.activate(initial)
                        else:
                            self.model.focused_property = key
                        return

            if hasattr(self.model, 'panel_rect') and self.model.panel_rect.collidepoint(mx, my):
                return

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
            columns = 12

            if mx < margin or my < margin:
                self.model.selected_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = [i for i in self.model.items.keys() if i != "image_item_not_found"]
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_item_id = item_ids[idx]
                else:
                    self.model.selected_item_id = None

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
            columns = 12

            if mx < margin or my < margin:
                self.model.hovered_item_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                item_ids = [i for i in self.model.items.keys() if i != "image_item_not_found"]
                idx = row * columns + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < columns and 0 <= idx < len(item_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_item_id = item_ids[idx]
                else:
                    self.model.hovered_item_id = None

        else:
            self.model.hovered_item_id = None