"""
Manejador de eventos para la toolbar de Partículas.
"""

import pygame
from roguelike_game.config.particles_config import reload_particles

class ParticlesToolBarPanelEventHandler:
    """Maneja eventos de la toolbar de Partículas."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event) -> bool:
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'particles_toolbar_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Tutorial toggle
            rect = icon_rects.get('tutorial_particles')
            if rect and rect.collidepoint(pos):
                if getattr(self.model, 'active_tool', None) == 'tutorial_particles':
                    self.model.active_tool = None
                    # Hook for future: deactivate tutorial panel
                    try:
                        tut = getattr(self.controller, 'particles_tutorial', None)
                        if tut:
                            tut.deactivate()
                    except Exception:
                        pass
                else:
                    self.model.active_tool = 'tutorial_particles'
                    # Hook for future: activate tutorial panel
                    try:
                        tut = getattr(self.controller, 'particles_tutorial', None)
                        if tut:
                            tut.activate()
                    except Exception:
                        pass
                return True
            # Undo / Redo (placeholders for future history)
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                return True
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                return True
            # Reload particles catalog and rebuild picker
            rect = icon_rects.get('particles_reload')
            if rect and rect.collidepoint(pos):
                try:
                    reload_particles()
                except Exception:
                    pass
                try:
                    picker = getattr(self.controller, 'particles_picker_controller', None)
                    if picker:
                        picker.rebuild()
                except Exception:
                    pass
                return True
            # Toggle principal (lista de partículas)
            rect = icon_rects.get('particles_list')
            if rect and rect.collidepoint(pos):
                if self.model.active_tool == 'particles_list':
                    self.model.active_tool = None
                else:
                    self.model.active_tool = 'particles_list'
                return True
        return False
