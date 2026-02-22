"""
Manejador de eventos para la toolbar de entidades (stub).
"""

import pygame
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP

class EntitiesToolBarPanelEventHandler:
    """
    Maneja eventos de la toolbar de entidades.
    """
    def __init__(self, controller, model):
        """
        Args:
            controller: Instancia del controlador de toolbar.
            model: Instancia del modelo de toolbar.
        """
        self.controller = controller
        self.model = model

    def handle_event(self, event):
        """
        Procesa eventos de click en la toolbar de entidades: toggle open/close de panels.
        """
        # Solo clicks izquierdo
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            pos = event.pos
            icon_rects = self.controller.toolbar_view.widget.icon_rects
            # Tutorial (toggle panel de tutorial)
            rect = icon_rects.get('tutorial_entities')
            if rect and rect.collidepoint(pos):
                tutorial = getattr(self.controller, 'tutorial_controller', None)
                if getattr(self.model, 'active_tool', None) == 'tutorial_entities':
                    # Apagar tutorial
                    self.model.active_tool = None
                    try:
                        if tutorial:
                            tutorial.deactivate()
                    except Exception:
                        pass
                else:
                    # Encender tutorial
                    self.model.active_tool = 'tutorial_entities'
                    try:
                        if tutorial:
                            tutorial.activate()
                    except Exception:
                        pass
                return True
            # Undo
            rect = icon_rects.get('undo')
            if rect and rect.collidepoint(pos):
                # Ejecutar undo si hay disponible
                if getattr(self.controller.history, 'undo', None):
                    if self.controller.history.undo():
                        try:
                            setattr(self.controller.model, 'tutorial_undo_pulse', True)
                        except Exception:
                            pass
                return True
            # Redo
            rect = icon_rects.get('redo')
            if rect and rect.collidepoint(pos):
                if getattr(self.controller.history, 'redo', None):
                    if self.controller.history.redo():
                        try:
                            setattr(self.controller.model, 'tutorial_redo_pulse', True)
                        except Exception:
                            pass
                return True
            # Evaluar herramienta de mapa
            rect = icon_rects.get(ENTITIES_TOOL_ON_MAP)
            if rect and rect.collidepoint(pos):
                tool = ENTITIES_TOOL_ON_MAP
                # Toggle activación
                if self.model.active_tool == tool:
                    # Desactivar
                    self.model.active_tool = None
                    # Ocultar panel Picker
                    self.controller.picker_controller.model.visible = False
                else:
                    # Activar
                    self.model.active_tool = tool
                    # Mostrar editor principal
                    editor = self.controller
                    editor.model.visible = True
                    # Mostrar panel Picker solo en mapa
                    editor.picker_controller.model.visible = True
                return True
        return False