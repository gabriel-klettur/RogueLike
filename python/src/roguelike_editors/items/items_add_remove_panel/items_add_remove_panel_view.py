"""
Vista para el sub-toolbar de añadir/eliminar Items.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image


class ItemsAddRemovePanelView:
    """
    Vista del panel de añadir/eliminar items.
    """
    def __init__(self, controller, model):
        self.controller = controller  # InventoryEditorController
        self.model = model
        # Posicionar a la derecha del toolbar principal de Items
        toolbar_view = getattr(self.controller, 'items_toolbar_view', None)
        if toolbar_view is not None:
            tb_widget = toolbar_view.widget
            panel_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
            panel_w, _ = tb_widget.panel.surface.get_size()
            self.x = panel_pos[0] + panel_w + 8
            self.y = panel_pos[1]
        else:
            self.x, self.y = 10, 80
        # Tamaño de iconos y espacio
        self.size = 64
        self.padding = 8

        icon_paths = {
            'add_item': 'assets/ui/add_item.png',
            'remove_item': 'assets/ui/remove_item.png',
            'add_item_on_system': 'assets/ui/add_item_on_system.png',
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

        self.widget = ToolbarView(
            controller=self.controller,
            items=self.model.tools,
            icons=self.icons,
            x=self.x,
            y=self.y,
            size=self.size,
            padding=self.padding,
            name='ItemsAddRemovePanel',
        )

    def render(self, screen):
        if not getattr(self.model, 'visible', False):
            return
        # Reposicionar dinámicamente junto al toolbar principal
        toolbar_view = getattr(self.controller, 'items_toolbar_view', None)
        if toolbar_view is not None:
            tb_widget = toolbar_view.widget
            panel_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
            panel_w, _ = tb_widget.panel.surface.get_size()
            self.widget.x = panel_pos[0] + panel_w + 8
            self.widget.y = panel_pos[1]
        self.widget.render(screen)
        # Parpadeo de borde en 'add_item' o 'remove_item' si el modo está activo
        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            # Add Item: borde amarillo cuando spawn_mode está activo y esta herramienta es la activa
            if getattr(self.controller.model, 'spawn_mode_active', False) and self.model.active_tool == 'add_item':
                rect = self.widget.icon_rects.get('add_item')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
            # Remove Item: borde rojo cuando delete_mode está activo y esta herramienta es la activa
            if getattr(self.controller.model, 'delete_mode_active', False) and self.model.active_tool == 'remove_item':
                rect = self.widget.icon_rects.get('remove_item')
                if rect:
                    pygame.draw.rect(screen, (255, 0, 0), rect.inflate(6, 6), 3)
            # Parpadeo de borde amarillo para 'add_item_on_system' si está activo
            if self.model.active_tool == 'add_item_on_system':
                rect = self.widget.icon_rects.get('add_item_on_system')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
        # Fondo semitransparente verde cuando 'add_item_on_system' está activo
        if self.model.active_tool == 'add_item_on_system':
            rect = self.widget.icon_rects.get('add_item_on_system')
            if rect:
                overlay = pygame.Surface((rect.width, rect.height), pygame.SRCALPHA)
                overlay.fill((0, 255, 0, 60))
                screen.blit(overlay, (rect.x, rect.y))

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        return self.widget.handle_event(event)

