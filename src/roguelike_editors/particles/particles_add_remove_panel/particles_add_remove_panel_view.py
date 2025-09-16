"""
Particles Add/Remove panel view.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import UI_MARGIN


class ParticlesAddRemovePanelView:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        self.size = 64
        self.padding = 8
        self.x, self.y = 10, 80
        icon_paths = {
            'particles_add_system': 'assets/ui/particles_editor/add_remove_panel/particles_add_system.png',
            'particles_add': 'assets/ui/particles_editor/add_remove_panel/particles_add.png',
            'particles_remove': 'assets/ui/particles_editor/add_remove_panel/particles_remove.png',
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
            name='ParticlesAddRemovePanel',
        )

    def render(self, screen):
        if not getattr(self.model, 'visible', False):
            return
        # Position to the right of the main particles toolbar
        toolbar_view = getattr(self.controller, 'particles_toolbar_view', None)
        if toolbar_view is not None:
            tb_widget = toolbar_view.widget
            panel_pos = tb_widget.panel.pos or (tb_widget.x, tb_widget.y)
            panel_w, _ = tb_widget.panel.surface.get_size()
            new_x = panel_pos[0] + panel_w + UI_MARGIN
            new_y = panel_pos[1]
            self.widget.x = new_x
            self.widget.y = new_y
            try:
                self.widget.panel.pos = (new_x, new_y)
            except Exception:
                pass
        self.widget.render(screen)

        # Optional blinking borders for active tools
        now = pygame.time.get_ticks()
        phase_on = ((now // 500) % 2) == 0
        if phase_on:
            if self.model.active_tool == 'particles_add_system':
                rect = self.widget.icon_rects.get('particles_add_system')
                if rect:
                    pygame.draw.rect(screen, (0, 255, 255), rect.inflate(6, 6), 3)
            if self.model.active_tool == 'particles_add':
                rect = self.widget.icon_rects.get('particles_add')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
            if self.model.active_tool == 'particles_remove':
                rect = self.widget.icon_rects.get('particles_remove')
                if rect:
                    pygame.draw.rect(screen, (255, 0, 0), rect.inflate(6, 6), 3)

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        return self.widget.handle_event(event)
