"""
Vista para la toolbar de Items.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image


class ItemsToolBarPanelView:
    """
    Vista de la toolbar de Items.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Posición: bajo el título del Inventory Editor o del Items Editor
        title_widget = getattr(getattr(self.controller, 'title_controller', None), 'view', None)
        title_widget = getattr(title_widget, 'widget', None)
        if title_widget is not None:
            title_text = title_widget.text or ""
            text_surf = title_widget.font.render(title_text, True, title_widget.text_color)
            bg_h = text_surf.get_height() + title_widget.padding_y * 2
            self.x = title_widget.x
            self.y = title_widget.y + bg_h + 8  # margen fijo
        else:
            # Intentar obtener title_rect desde ItemsEditor picker view
            picker_view = getattr(getattr(self.controller, 'picker_controller', None), 'view', None)
            title_rect = getattr(picker_view, 'title_rect', None)
            if title_rect is not None:
                self.x = 10
                self.y = title_rect.bottom + 8
            else:
                self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8

        icon_paths = {
            'items_on_map': 'assets/ui/items_on_map_icon.png',
            'undo': 'assets/ui/undo.png',
            'redo': 'assets/ui/redo.png',
            'tutorial_items': 'assets/ui/tutorials_button.png',
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
            name='ItemsToolBarPanel',
        )

    def render(self, screen):
        # Recalcular posición para centrar el botón 'items_on_map'
        try:
            sw = screen.get_width()
        except Exception:
            sw = 1280
        # Ancho total del toolbar (3 iconos)
        total_w = self.size * len(self.model.tools) + self.padding * (len(self.model.tools) - 1)
        # Queremos que 'items_on_map' quede centrado; si está en el índice 1, basta con centrar el total
        # Si no, igualmente centramos todo el grupo
        self.widget.x = (sw - total_w) // 2
        # Mantener y bajo el título (Inventory o Items Editor)
        title_widget = getattr(getattr(self.controller, 'title_controller', None), 'view', None)
        title_widget = getattr(title_widget, 'widget', None)
        if title_widget is not None:
            title_text = title_widget.text or ""
            text_surf = title_widget.font.render(title_text, True, title_widget.text_color)
            bg_h = text_surf.get_height() + title_widget.padding_y * 2
            self.widget.y = title_widget.y + bg_h + 8
        else:
            picker_view = getattr(getattr(self.controller, 'picker_controller', None), 'view', None)
            title_rect = getattr(picker_view, 'title_rect', None)
            if title_rect is not None:
                self.widget.y = title_rect.bottom + 8
            else:
                self.widget.y = self.y
        self.widget.render(screen)

    def handle_event(self, event):
        return self.widget.handle_event(event)

