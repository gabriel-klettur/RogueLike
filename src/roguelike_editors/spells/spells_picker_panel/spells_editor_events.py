import pygame
import os
from roguelike_ui.services.json_persistence import save_to_json, load_from_json, remove_from_json

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
        # Inline text input is now handled by SpellsPropertiesPanelController

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
            # Respect picker visibility
            if not getattr(self.model, 'picker_visible', False):
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
            # top offset matches view (title height aware)
            title_rect = getattr(self.view, 'title_rect', None)
            grid_top = max(margin, (title_rect.bottom + 10) if title_rect else margin)
            # determine clicked cell
            if mx < margin or my < grid_top:
                clicked_sid = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - grid_top + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = grid_top + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < cols and 0 <= idx < len(spell_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    clicked_sid = spell_ids[idx]
                else:
                    clicked_sid = None

            # If delete mode active, process deletion
            if getattr(self.model, 'delete_mode_active', False):
                if clicked_sid:
                    # Persist deletion
                    path = os.path.join(os.getcwd(), "data", "spells", "spells.json")
                    removed = remove_from_json(path, clicked_sid)
                    if removed:
                        if clicked_sid in self.model.spells:
                            del self.model.spells[clicked_sid]
                        if clicked_sid in self.model.assets:
                            del self.model.assets[clicked_sid]
                    # exit delete mode
                    self.model.delete_mode_active = False
                    # sync AR panel state
                    ar_model = getattr(self.controller, 'spells_add_remove_model', None)
                    if ar_model is not None:
                        ar_model.active_tool = None
                    self.model.selected_id = None
                else:
                    # Clicked outside: cancel delete mode
                    self.model.delete_mode_active = False
                    ar_model = getattr(self.controller, 'spells_add_remove_model', None)
                    if ar_model is not None:
                        ar_model.active_tool = None
                return

            # Normal selection flow
            self.model.selected_id = clicked_sid
            return

        # Mouse hover
        if event.type == pygame.MOUSEMOTION and self.model.visible:
            mx, my = event.pos
            if not getattr(self.model, 'picker_visible', False):
                self.model.hovered_id = None
                return
            margin = 20
            cell_size = 64
            text_margin = 4
            font_h = self.view.font.get_height()
            cell_height = cell_size + text_margin + font_h
            cols = 8
            title_rect = getattr(self.view, 'title_rect', None)
            grid_top = max(margin, (title_rect.bottom + 10) if title_rect else margin)
            if mx < margin or my < grid_top:
                self.model.hovered_id = None
            else:
                col = (mx - margin) // (cell_size + margin)
                row = (my - grid_top + self.model.scroll_index * (cell_height + margin)) // (cell_height + margin)
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = margin + col * (cell_size + margin)
                y0 = grid_top + (row - self.model.scroll_index) * (cell_height + margin)
                if 0 <= col < cols and 0 <= idx < len(spell_ids) and x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size:
                    self.model.hovered_id = spell_ids[idx]
                else:
                    self.model.hovered_id = None
            return

        # Reset hover
        self.model.hovered_id = None
