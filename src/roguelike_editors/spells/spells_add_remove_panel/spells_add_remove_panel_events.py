"""
Spells Add/Remove panel events.
"""

import pygame


class SpellsAddRemovePanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'spells_add_remove_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Add
            rect_add = icon_rects.get('add_spell')
            if rect_add and rect_add.collidepoint(pos):
                # Toggle ADD mode (pending duplicate selection). Actual duplication
                # will occur when the user clicks a spell in the picker grid.
                if self.model.active_tool == 'add_spell':
                    self.model.active_tool = None
                    # Ensure delete mode is off when leaving add mode
                    try:
                        self.controller.model.delete_mode_active = False
                    except Exception:
                        pass
                else:
                    self.model.active_tool = 'add_spell'
                    # Turn off delete mode when entering add mode
                    try:
                        self.controller.model.delete_mode_active = False
                    except Exception:
                        pass
                    # Ensure the picker is visible so the user can select a base spell
                    try:
                        self.controller.model.picker_visible = True
                    except Exception:
                        pass
                return True
            # Remove toggle
            rect_del = icon_rects.get('remove_spell')
            if rect_del and rect_del.collidepoint(pos):
                # Toggle active tool
                if self.model.active_tool == 'remove_spell':
                    self.model.active_tool = None
                    self.controller.model.delete_mode_active = False
                else:
                    self.model.active_tool = 'remove_spell'
                    self.controller.model.delete_mode_active = True
                return True
        return False

