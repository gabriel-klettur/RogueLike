import pygame
import os
from roguelike_ui.services.json_persistence import save_to_json, load_from_json

class SpellEditorEventHandler:
    """
    Event handler for the Spell Editor UI.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.text_input = controller.text_input
        self.dc_detector = controller.dc_detector

    def handle(self, event: pygame.event.Event) -> None:
        # Inline text input handling
        if self.text_input.active:
            if self.text_input.handle_event(event):
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                if not self.text_input.active:
                    self.controller._commit_edit()
                return
            return

        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F4:
                # Toggle visibility
                self.model.visible = not self.model.visible
                self.model.selected_id = None
                return
            if not self.model.visible:
                return
            if event.key == pygame.K_UP:
                self.model.scroll_index = max(0, self.model.scroll_index - 1)
                return
            if event.key == pygame.K_DOWN:
                self.model.scroll_index += 1
                return

        # Mouse click
        if event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            # Click on property entries
            for rect, key in getattr(self.model, 'property_entries', []):
                if rect.collidepoint(mx, my):
                    if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                        self.model.focused_property = key
                        self.model.editing_property = key
                        sid = self.model.selected_id or self.model.hovered_id
                        if sid:
                            val = self.model.spells.get(sid, {}).get(key, "")
                            self.model.editing_text = str(val)
                            self.model.editing_cursor = len(self.model.editing_text)
                            self.text_input.activate(self.model.editing_text)
                        return
                    else:
                        self.model.focused_property = key
                        return
            # Click on grid
            screen = pygame.display.get_surface()
            sw, sh = screen.get_size() if screen else (0, 0)
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            cols = 8
            # determine clicked cell
            if mx < margin or my < margin:
                self.model.selected_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < cols and 0 <= idx < len(spell_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.selected_id = spell_ids[idx]
                else:
                    self.model.selected_id = None
            self.model.focused_property = None
            self.model.editing_property = None
            return

        # Mouse hover
        if event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            cols = 8
            if mx < margin or my < margin:
                self.model.hovered_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - margin + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = margin + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < cols and 0 <= idx < len(spell_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_id = spell_ids[idx]
                else:
                    self.model.hovered_id = None
            return

        # Reset hover
        self.model.hovered_id = None
