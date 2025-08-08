"""
Vista para la toolbar de entidades (stub).
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP, UI_MARGIN

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
        # Configuración de la toolbar alineada bajo el title panel
        title_widget = self.controller.title_controller.view.widget
        # Posición x igual al del title panel
        self.x = title_widget.x
        # Calcular y justo debajo del title panel con margen de 8px
        title_text = title_widget.text or ""
        text_surf = title_widget.font.render(title_text, True, title_widget.text_color)
        bg_h = text_surf.get_height() + title_widget.padding_y * 2
        self.y = title_widget.y + bg_h + UI_MARGIN
        self.size = 64
        self.padding = 8
        # Crear iconos vacíos para cada herramienta
        icon_paths = {
            ENTITIES_TOOL_ON_MAP: 
            'assets/ui/entities_on_map_icon.png',
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
            padding=self.padding,
            name='EntitiesToolBarPanel'
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