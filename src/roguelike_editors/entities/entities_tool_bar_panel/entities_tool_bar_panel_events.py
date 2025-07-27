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
            # Evaluar solo herramientas de mapa o sistema
            for tool in ('entities_on_map', 'entities_on_system'):
                rect = self.controller.toolbar_view.widget.icon_rects.get(tool)
                if rect and rect.collidepoint(pos):
                    # Toggle activación
                    if self.model.active_tool == tool:
                        # Desactivar
                        self.model.active_tool = None
                        # Ocultar panels
                        editor = self.controller
                        # panel position reset removed
                        editor.controller.model.visible = False
                    else:
                        # Activar
                        self.model.active_tool = tool
                        # Mostrar panels
                        editor = self.controller
                        editor.controller.model.visible = True
                    return True
        return False