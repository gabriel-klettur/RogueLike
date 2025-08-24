"""
Spells Add/Remove panel view.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsAddRemovePanelView:
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        self.size = 64
        self.padding = 8
        self.x, self.y = 10, 80
        icon_paths = {
            'add_spell': 'assets/ui/add_spell.png',
            'remove_spell': 'assets/ui/remove_spell.png',
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
            name='SpellsAddRemovePanel',
        )

    def render(self, screen):
        if not getattr(self.model, 'visible', False):
            return
        # Position to the right of main toolbar if available
        toolbar_view = getattr(self.controller, 'spells_toolbar_view', None)
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
        # Blink border for active modes
        now = pygame.time.get_ticks()
        if (now // 500) % 2 == 0:
            if self.model.active_tool == 'add_spell':
                rect = self.widget.icon_rects.get('add_spell')
                if rect:
                    pygame.draw.rect(screen, (255, 255, 0), rect.inflate(6, 6), 3)
            if self.model.active_tool == 'remove_spell':
                rect = self.widget.icon_rects.get('remove_spell')
                if rect:
                    pygame.draw.rect(screen, (255, 0, 0), rect.inflate(6, 6), 3)

    def handle_event(self, event):
        if not getattr(self.model, 'visible', False):
            return False
        return self.widget.handle_event(event)

