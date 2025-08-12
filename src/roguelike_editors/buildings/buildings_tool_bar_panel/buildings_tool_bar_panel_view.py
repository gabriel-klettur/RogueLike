"""
Vista para la toolbar de Buildings.
"""

import pygame
from roguelike_ui.widgets.toolbar_panel import ToolbarView
from roguelike_engine.utils.loader import load_image


class BuildingsToolBarPanelView:
    """
    Vista de la toolbar de Buildings.
    """
    def __init__(self, controller, model):
        self.controller = controller
        self.model = model

        # Posición: bajo el título del Buildings Editor
        self.x, self.y = 10, 10
        self.size = 64
        self.padding = 8

        icon_paths = {
            'buildings_manager': 'assets/ui/building_manager_icon.png',
            'buildings_colliders': 'assets/ui/buildings_colliders.png',
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
            name='BuildingsToolBarPanel',
        )

    def _compute_position(self, screen: pygame.Surface) -> None:
        # Valor por defecto: centrado horizontal (fallback)
        try:
            sw = screen.get_width()
        except Exception:
            sw = 1280
        try:
            panel_w = self.widget.panel.surface.get_width()
        except Exception:
            panel_w = self.size + 16  # edge_padding por defecto en ToolbarView = 8
        desired_x = (sw - panel_w) // 2
        # Y: justo debajo del título del Buildings Editor (si está disponible)
        title_rect = None
        try:
            editor_view = getattr(self.controller, 'editor_view', None)
            # 1) Preferir el rect expuesto por el render del título
            title_rect = getattr(editor_view, '_last_title_rect', None)
            # 2) Fallback: usar el rect del widget del título si existe
            if title_rect is None and editor_view and hasattr(editor_view, 'title_view'):
                title_widget = getattr(editor_view.title_view, 'widget', None)
                if title_widget is not None and hasattr(title_widget, 'rect'):
                    title_rect = title_widget.rect
        except Exception:
            title_rect = None
        # Alinear con el título: debajo y alineado a la IZQUIERDA del título
        if title_rect is not None:
            desired_x = title_rect.left
            desired_y = title_rect.bottom + 8
        else:
            desired_y = self.y
        # MUY IMPORTANTE: ToolbarView usa panel.pos, no self.widget.x/y
        try:
            self.widget.panel.pos = (desired_x, desired_y)
        except Exception:
            # Fallback por si no existe panel
            self.widget.x = desired_x
            self.widget.y = desired_y

    def render(self, screen: pygame.Surface) -> None:
        self._compute_position(screen)
        self.widget.render(screen)

    def handle_event(self, event) -> bool:
        return self.widget.handle_event(event)

