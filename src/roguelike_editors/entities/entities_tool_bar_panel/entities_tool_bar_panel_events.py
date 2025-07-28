"""
Manejador de eventos para la toolbar de entidades (stub).
"""

import pygame

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
            # Evaluar solo herramienta de mapa
            for tool in ('entities_on_map',):
                rect = self.controller.toolbar_view.widget.icon_rects.get(tool)
                if rect and rect.collidepoint(pos):
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
                        editor.picker_controller.model.visible = (tool == 'entities_on_map')
                    return True
        return False