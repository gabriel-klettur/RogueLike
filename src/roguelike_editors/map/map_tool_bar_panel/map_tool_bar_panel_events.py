import pygame
import logging
from .map_tool_bar_panel_view import TOOLS

logger = logging.getLogger(__name__)


class MapToolBarPanelEvents:
    def __init__(self, controller, model=None):
        self.controller = controller
        self.model = model or getattr(controller, 'model', None)

    def handle_click(self, mouse_pos) -> bool:
        """
        Procesa clics del toolbar usando los rects calculados por ToolbarView.
        Si aún no existen, los calcula basándose en la geometría del widget.
        También maneja el dropdown de capas cuando está abierto.
        """
        c = self.controller
        editor = c.editor

        # Asegurar icon_rects incluso antes del primer render
        if not c.icon_rects:
            widget = getattr(getattr(c, 'view', None), 'widget', None)
            if widget and getattr(widget, 'icon_rects', None):
                c.icon_rects = dict(widget.icon_rects)
            elif widget:
                edge = getattr(widget, 'edge_padding', 8)
                panel_pos = widget.panel.pos or (widget.x, widget.y)
                size, pad = widget.size, widget.padding
                c.icon_rects = {}
                for idx, tool_name in enumerate(TOOLS):
                    local = pygame.Rect(edge, edge + idx * (size + pad), size, size)
                    c.icon_rects[tool_name] = local.move(panel_pos)

        # Handlers por herramienta
        def _toggle_pair(primary: str, disable: list[str]):
            c._toggle_mode(primary, disable=disable)
            logger.debug(f"[DEBUG][Toolbar/Events] {primary} -> {getattr(editor, primary)}")

        for tool_name, rect in c.icon_rects.items():
            if rect and rect.collidepoint(mouse_pos):
                if tool_name == "view_layers":
                    editor.layers_view_open = not editor.layers_view_open
                    logger.debug(f"[DEBUG][Toolbar/Events] layers_view_open -> {editor.layers_view_open}")
                    return True
                if tool_name == "add_zone":
                    # Delegate to the Add Zone tool controller, which enforces exclusivity
                    c.add_zone.toggle()
                    return True
                if tool_name == "delete_zone":
                    _toggle_pair("delete_zone_mode", ["add_zone_mode", "paint_tiles_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "paint_tiles":
                    _toggle_pair("paint_tiles_mode", ["add_zone_mode", "delete_zone_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "clear_colliders":
                    c.clear_colliders.toggle()
                    return True
                if tool_name == "paint_colliders":
                    _toggle_pair("paint_colliders_mode", ["add_zone_mode", "delete_zone_mode", "paint_tiles_mode", "clear_colliders_mode"])
                    return True

        # Dropdown de capas
        if editor.layers_view_open:
            for key, rect in c.option_rects.items():
                if rect and rect.collidepoint(mouse_pos):
                    c._handle_dropdown_selection(key)
                    return True

        return False
