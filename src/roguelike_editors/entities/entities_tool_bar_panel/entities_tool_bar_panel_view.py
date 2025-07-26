"""
Vista para la toolbar de entidades (stub).
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image

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
        self.size = 64
        self.padding = 8
        # Crear iconos vacíos para cada herramienta
        icon_paths = {
            'entities_on_map': 'assets/ui/entities_on_map_icon.png',
            'entities_on_system': 'assets/ui/entities_on_system_icon.png',
            'respawns': 'assets/ui/respawn.png',
            'undo': 'assets/ui/undo.png',
            'redo': 'assets/ui/redo.png',
        }
        self.icons = {}
        for tool in self.model.tools:
            path = icon_paths.get(tool)
            if path:
                try:
                    img = load_image(path, scale=(self.size, self.size))
                except FileNotFoundError:
                    img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                    img.fill((100, 100, 100, 150))
            else:
                img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                img.fill((100, 100, 100, 150))
            self.icons[tool] = img
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