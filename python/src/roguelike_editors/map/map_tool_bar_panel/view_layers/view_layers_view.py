import logging
import pygame
from pygame import Rect
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H

logger = logging.getLogger(__name__)


class ViewLayersView:
    """
    Vista para el dropdown de 'view_layers'.
    - Dibuja el dropdown anclado al ToolbarView (soporta dragging).
    - Calcula y expone rectángulos clicables a través del modelo.
    - Colorea cada opción en función del estado de visibilidad del editor.
    """
    COLOR_TEXT = (255, 255, 255)

    def __init__(self, controller, model):
        self.controller = controller
        self.model = model
        # Fuente específica para el dropdown
        pygame.font.init()
        self.font_dropdown = pygame.font.SysFont("Arial", 14)

    def render_dropdown(self, screen) -> None:
        """Renderiza el dropdown si está abierto."""
        if not self.controller.is_open():
            # Asegurarse de no dejar rects obsoletos
            self.model.option_rects.clear()
            return

        toolbar = self.controller.toolbar
        editor = self.controller.editor
        # Anclar al ToolbarView (posición y ancho actuales)
        tv = getattr(getattr(toolbar, "view", None), "widget", None)
        panel = getattr(tv, "panel", None)
        if panel and getattr(panel, "pos", None):
            panel_pos = panel.pos
            panel_w = panel.surface.get_width()
        else:
            panel_pos = (toolbar.x, toolbar.y)
            panel_w = toolbar.size + 2 * getattr(tv, "edge_padding", 8) if tv else toolbar.size + 16

        drop_x = panel_pos[0] + panel_w + toolbar.padding
        drop_y = panel_pos[1]

        # Claves: "show_all", "hide_all", cada Layer, "buildings", "colliders"
        keys = ["show_all", "hide_all"] + list(Layer) + ["buildings", "colliders"]
        self.model.option_rects.clear()

        for idx, key in enumerate(keys):
            ry = drop_y + idx * BTN_H
            rect = Rect(drop_x, ry, BTN_W, BTN_H)
            self.model.option_rects[key] = rect

            # Fondo
            pygame.draw.rect(screen, (20, 20, 20), rect)

            # Borde según tipo/estado
            if key in ("show_all", "hide_all"):
                border_color = self.COLOR_TEXT
            elif isinstance(key, Layer):
                border_color = (0, 255, 0) if editor.visible_layers[key] else (255, 0, 0)
            elif key == "buildings":
                border_color = (128, 0, 128) if editor.show_buildings else (255, 0, 0)
            else:  # "colliders"
                border_color = (255, 255, 0) if editor.show_colliders else (255, 0, 0)

            pygame.draw.rect(screen, border_color, rect, 2)

            # Texto
            if key == "show_all":
                text = "Show All"
            elif key == "hide_all":
                text = "Hide All"
            elif isinstance(key, Layer):
                text = key.name
            elif key == "buildings":
                text = "Buildings"
            else:
                text = "Colliders"

            text_surf = self.font_dropdown.render(text, True, self.COLOR_TEXT)
            screen.blit(text_surf, (drop_x + 5, ry + (BTN_H - text_surf.get_height()) // 2))
