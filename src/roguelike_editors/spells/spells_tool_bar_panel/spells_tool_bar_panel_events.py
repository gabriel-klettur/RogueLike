"""
Manejador de eventos para la toolbar de Spells.
"""

import pygame


class SpellsToolBarPanelEventHandler:
    """Maneja eventos de la toolbar de Spells."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

    def handle_event(self, event) -> bool:
        if event.type == pygame.MOUSEBUTTONDOWN and getattr(event, 'button', None) == 1:
            pos = event.pos
            toolbar_view = getattr(self.controller, 'spells_toolbar_view', None)
            widget = getattr(toolbar_view, 'widget', None)
            icon_rects = getattr(widget, 'icon_rects', {}) if widget else {}
            # Tutorial (toggle)
            rect = icon_rects.get('tutorial_spells')
            if rect and rect.collidepoint(pos):
                tutorial = getattr(self.controller, 'spells_tutorial', None)
                if getattr(self.model, 'active_tool', None) == 'tutorial_spells':
                    self.model.active_tool = None
                    try:
                        if tutorial:
                            tutorial.deactivate()
                    except Exception:
                        pass
                else:
                    self.model.active_tool = 'tutorial_spells'
                    try:
                        if tutorial:
                            tutorial.activate()
                    except Exception:
                        pass
                return True
            # Undo (placeholder)
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                return True
            # Redo (placeholder)
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                return True
            # Toggle principal
            rect = icon_rects.get('spells_on_map')
            if rect and rect.collidepoint(pos):
                if self.model.active_tool == 'spells_on_map':
                    # Desactivar: ocultar picker y panel add/remove
                    self.model.active_tool = None
                    self.controller.model.picker_visible = False
                    # Salir de delete mode
                    if hasattr(self.controller.model, 'delete_mode_active'):
                        self.controller.model.delete_mode_active = False
                    arm = getattr(self.controller, 'spells_add_remove_model', None)
                    if arm is not None:
                        arm.visible = False
                        arm.active_tool = None
                else:
                    # Activar: mostrar picker y panel add/remove
                    self.model.active_tool = 'spells_on_map'
                    self.controller.model.picker_visible = True
                    # Salir de delete mode por limpieza
                    if hasattr(self.controller.model, 'delete_mode_active'):
                        self.controller.model.delete_mode_active = False
                    arm = getattr(self.controller, 'spells_add_remove_model', None)
                    if arm is not None:
                        arm.visible = True
                        arm.active_tool = None
                return True
        return False

