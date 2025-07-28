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
        # Posición inicial: a la derecha del toolbar de entidades
        toolbar_widget = self.controller.toolbar_view.widget
        panel_pos = toolbar_widget.panel.pos or (toolbar_widget.x, toolbar_widget.y)
        panel_w, _ = toolbar_widget.panel.surface.get_size()
        margin = 8
        self.x = panel_pos[0] + panel_w + margin
        self.y = panel_pos[1]
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
            padding=self.padding,
            name='EntitiesAddRemovePanel'
        )

    def render(self, screen):

        # Renderizar panel de añadir/eliminar
        self.widget.render(screen)
        # Parpadeo de borde en 'add_entitie' o 'remove_entitie' si mode activo
        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            if self.controller.model.spawn_mode_active and self.model.active_tool == 'add_entitie':
                rect = self.widget.icon_rects.get('add_entitie')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
            if self.controller.model.delete_mode_active and self.model.active_tool == 'remove_entitie':
                rect = self.widget.icon_rects.get('remove_entitie')
                if rect:
                    pygame.draw.rect(screen, (255, 0, 0), rect.inflate(6, 6), 3)

    def handle_event(self, event):
        return self.widget.handle_event(event)
