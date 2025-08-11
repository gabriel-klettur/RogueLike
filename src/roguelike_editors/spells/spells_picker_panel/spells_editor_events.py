import pygame
import os
from roguelike_ui.services.json_persistence import save_to_json, load_from_json, remove_from_json
from roguelike_editors.entities.services.constants import UI_MARGIN

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

        # First, allow panel dragging like Entities picker (right mouse button)
        if self._handle_panel_drag(event):
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
            # Respect picker visibility
            if not getattr(self.model, 'picker_visible', False):
                return
            # Only process clicks inside panel rect (like Entities picker)
            if self.model.panel_rect and not self.model.panel_rect.collidepoint(event.pos):
                return

            # Use view config to match rendering
            margin = getattr(self.view, 'margin', 20)
            cell_size = getattr(self.view, 'cell_size', 64)
            text_margin = getattr(self.view, 'text_margin', 4)
            cols = getattr(self.view, 'columns', 10)
            fh = self.view.font.get_height()
            ch = cell_size + text_margin + fh

            # Anchors (the view pins x/y to anchors already)
            ox, oy = getattr(self.view, 'x', 0), getattr(self.view, 'y', 0)
            header_height = 0  # no tabs/header in spells picker

            # Relative mouse pos inside grid
            mx_rel = mx - (ox + margin)
            my_rel = my - (oy + margin + header_height)
            if mx_rel < 0 or my_rel < 0:
                clicked_sid = None
            else:
                col = mx_rel // (cell_size + margin)
                row = my_rel // (ch + margin) + self.model.scroll_index
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = ox + margin + col * (cell_size + margin)
                y0 = oy + margin + header_height + (row - self.model.scroll_index) * (ch + margin)
                in_x = x0 <= mx <= x0 + cell_size
                in_y = y0 <= my <= y0 + ch
                clicked_sid = spell_ids[idx] if (0 <= col < cols and 0 <= idx < len(spell_ids) and in_x and in_y) else None

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
            # Only update hover within panel rect
            if self.model.panel_rect and not self.model.panel_rect.collidepoint(event.pos):
                self.model.hovered_id = None
                return

            margin = getattr(self.view, 'margin', 20)
            cell_size = getattr(self.view, 'cell_size', 64)
            text_margin = getattr(self.view, 'text_margin', 4)
            cols = getattr(self.view, 'columns', 10)
            fh = self.view.font.get_height()
            ch = cell_size + text_margin + fh

            ox, oy = getattr(self.view, 'x', 0), getattr(self.view, 'y', 0)
            header_height = 0
            mx_rel = mx - (ox + margin)
            my_rel = my - (oy + margin + header_height)
            if mx_rel < 0 or my_rel < 0:
                self.model.hovered_id = None
            else:
                col = mx_rel // (cell_size + margin)
                row = my_rel // (ch + margin) + self.model.scroll_index
                spell_ids = list(self.model.spells.keys())
                idx = row * cols + col
                x0 = ox + margin + col * (cell_size + margin)
                y0 = oy + margin + header_height + (row - self.model.scroll_index) * (ch + margin)
                in_x = x0 <= mx <= x0 + cell_size
                in_y = y0 <= my <= y0 + ch
                self.model.hovered_id = spell_ids[idx] if (0 <= col < cols and 0 <= idx < len(spell_ids) and in_x and in_y) else None
            return

        # Reset hover
        self.model.hovered_id = None

    def _handle_panel_drag(self, event: pygame.event.Event) -> bool:
        """Support right-click dragging of the picker panel, mirroring Entities picker."""
        if not self.model.visible or not getattr(self.model, 'picker_visible', False):
            return False

        # Start drag with RMB within the panel rect
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if self.model.panel_rect and self.model.panel_rect.collidepoint(event.pos):
                # Use entire panel as header for dragging
                self.view.draggable_panel.handle_event(event, header_rect=self.model.panel_rect)
                return True

        # Continue dragging
        if event.type == pygame.MOUSEMOTION and getattr(self.view.draggable_panel, 'dragging', False):
            self.view.draggable_panel.handle_event(event)
            return True

        # End dragging
        if event.type == pygame.MOUSEBUTTONUP and getattr(self.view.draggable_panel, 'dragging', False):
            self.view.draggable_panel.handle_event(event)
            return True

        return False
