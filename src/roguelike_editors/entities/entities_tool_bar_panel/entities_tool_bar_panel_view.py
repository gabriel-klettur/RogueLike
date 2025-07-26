"""
Vista para la toolbar de entidades (stub).
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView

class EntitiesToolBarPanelView:
    """
    Vista de la toolbar de entidades.
    """
    def __init__(self, controller, model):
        """
        Args:
            controller: Instancia del controlador de toolbar.
            model: Instancia del modelo de toolbar.
        """
        self.controller = controller
        self.model = model
        # Configuración de la toolbar
        self.x = 10
        self.y = 10
        self.size = 32
        self.padding = 4
        # Crear iconos vacíos para cada herramienta
        self.icons = {}
        for tool in self.model.tools:
            surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
            surf.fill((100, 100, 100, 150))
            self.icons[tool] = surf
        # Instanciar widget genérico de toolbar
        self.widget = ToolbarView(
            controller=self.controller,
            items=self.model.tools,
            icons=self.icons,
            x=self.x,
            y=self.y,
            size=self.size,
            padding=self.padding
        )

    def render(self, screen):
        """
        Dibuja el toolbar de entidades usando el widget genérico.
        """
        self.widget.render(screen)

    def handle_event(self, event):
        """
        Maneja eventos de la toolbar de entidades.
        """
        return self.widget.handle_event(event)