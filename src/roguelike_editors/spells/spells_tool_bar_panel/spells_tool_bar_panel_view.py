"""
Vista para la toolbar de Spells.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image
from roguelike_editors.entities.services.constants import UI_MARGIN


class SpellsToolBarPanelView:
    """Vista de la toolbar de Spells, posicionada bajo la barra de título."""
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Posición por defecto; se recalcula en render
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8
        icon_paths = {
            'spells_on_map': 'assets/ui/spells_on_map_icon.png',
            'undo': 'assets/ui/undo.png',
            'redo': 'assets/ui/redo.png',
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
            name='SpellsToolBarPanel',
        )

    def render(self, screen: pygame.Surface):
        # Centrar horizontalmente el grupo de iconos
        try:
            sw = screen.get_width()
        except Exception:
            sw = 1280
        total_w = self.size * len(self.model.tools) + self.padding * (len(self.model.tools) - 1)
        self.widget.x = (sw - total_w) // 2
        # Posicionar bajo el título si existe
        title_widget = getattr(getattr(self.controller, 'title_controller', None), 'view', None)
        title_widget = getattr(title_widget, 'widget', None)
        if title_widget is not None:
            title_text = title_widget.text or ""
            text_surf = title_widget.font.render(title_text, True, title_widget.text_color)
            bg_h = text_surf.get_height() + title_widget.padding_y * 2
            self.widget.y = title_widget.y + bg_h + UI_MARGIN
        else:
            # Fallback: usar title_rect del picker/view del editor
            picker_view = getattr(self.controller, 'picker_controller', None)
            if picker_view is None:
                picker_view = getattr(self.controller, 'editor_controller', None)
            picker_view = getattr(picker_view, 'view', None)
            title_rect = getattr(picker_view, 'title_rect', None)
            if title_rect is not None:
                self.widget.y = title_rect.bottom + UI_MARGIN
            else:
                self.widget.y = self.y
        self.widget.render(screen)

    def handle_event(self, event):
        return self.widget.handle_event(event)

