"""
Particles Add/Remove panel events.
"""

import pygame


class ParticlesAddRemovePanelEventHandler:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            view = getattr(self.controller, 'particles_add_remove_view', None)
            widget = getattr(view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}

            # Add to system
            rect_sys = icon_rects.get('particles_add_system')
            if rect_sys and rect_sys.collidepoint(pos):
                self.model.active_tool = (
                    None if self.model.active_tool == 'particles_add_system' else 'particles_add_system'
                )
                # Future hook: open creation dialog or capture current selection to create system entry
                try:
                    self.controller.model.delete_mode_active = False
                    # Also reflect on picker's model if available
                    try:
                        picker = getattr(self.controller, 'particles_picker_controller', None)
                        if picker is not None:
                            picker.model.delete_mode_active = False
                    except Exception:
                        pass
                except Exception:
                    pass
                return True

            # Add to map (place particle instance)
            rect_add = icon_rects.get('particles_add')
            if rect_add and rect_add.collidepoint(pos):
                if self.model.active_tool == 'particles_add':
                    self.model.active_tool = None
                    try:
                        self.controller.model.delete_mode_active = False
                        # Ensure picker is visible to choose a particle preset
                        self.controller.model.picker_visible = True
                        # Turn OFF add-mode blinking
                        try:
                            picker = getattr(self.controller, 'particles_picker_controller', None)
                            if picker is not None:
                                picker.model.add_mode_active = False
                        except Exception:
                            pass
                    except Exception:
                        pass
                else:
                    self.model.active_tool = 'particles_add'
                    try:
                        self.controller.model.delete_mode_active = False
                        # Ensure picker is visible to choose a particle preset
                        self.controller.model.picker_visible = True
                        # Reflect on picker's model
                        try:
                            picker = getattr(self.controller, 'particles_picker_controller', None)
                            if picker is not None:
                                picker.model.delete_mode_active = False
                                picker.model.add_mode_active = True
                        except Exception:
                            pass
                    except Exception:
                        pass
                return True

            # Remove from map and picker
            rect_del = icon_rects.get('particles_remove')
            if rect_del and rect_del.collidepoint(pos):
                if self.model.active_tool == 'particles_remove':
                    self.model.active_tool = None
                    try:
                        self.controller.model.delete_mode_active = False
                    except Exception:
                        pass
                else:
                    self.model.active_tool = 'particles_remove'
                    try:
                        self.controller.model.delete_mode_active = True
                        # Reflect on picker's model so it handles deletions
                        try:
                            picker = getattr(self.controller, 'particles_picker_controller', None)
                            if picker is not None:
                                picker.model.delete_mode_active = True
                                picker.model.add_mode_active = False
                        except Exception:
                            pass
                    except Exception:
                        pass
                return True
        return False
