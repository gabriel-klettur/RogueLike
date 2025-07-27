import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image

class EntitiesAddRemovePanelView:
    """
    Vista para el panel de añadir/eliminar entidades.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Posición inicial (igual que el toolbar principal o ajustada)
        self.x = 10
        self.y = 10 + 64 + 8  # posición debajo del toolbar principal
        # Tamaño de iconos y espacio
        self.size = 64
        self.padding = 8

        # Rutas de los iconos
        icon_paths = {
            'add_entitie': 'assets/ui/add_entitie.png',
            'remove_entitie': 'assets/ui/remove_entitie.png',
        }
        self.icons = {}
        for tool in self.model.tools:
            path = icon_paths.get(tool)
            if path:
                try:
                    img = load_image(path, scale=(self.size, self.size))
                except Exception:
                    img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                    img.fill((100, 100, 100, 150))
            else:
                img = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
                img.fill((100, 100, 100, 150))
            self.icons[tool] = img

        # Widget genérico de toolbar
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
        # Alinear dinámicamente junto al botón 'entities_on_map'
        toolbar_widget = self.controller.toolbar_controller.view.widget
        map_rect = toolbar_widget.icon_rects.get('entities_on_map')
        if map_rect:
            margin_between = 8
            new_x = map_rect.right + margin_between
            new_y = map_rect.y
            # Actualizar posición del panel
            self.widget.panel.pos = (new_x, new_y)
            self.x = new_x
            self.y = new_y
        # Renderizar panel de añadir/eliminar
        self.widget.render(screen)

    def handle_event(self, event):
        return self.widget.handle_event(event)
