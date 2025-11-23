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
        También delega el dropdown de capas al MVC de ViewLayers cuando está abierto.
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
                if tool_name == "map_tutorial":
                    try:
                        tm = getattr(c, 'editor_manager', None)
                        if tm and getattr(tm, 'tutorial', None):
                            tm.tutorial.toggle()
                            return True
                    except Exception:
                        return True
                if tool_name == "view_layers":
                    # Delegar al controlador ViewLayers (mantiene consistencia y exclusividad si aplica)
                    opened = c.view_layers.toggle()
                    # Tutorial pulse: layers view opened
                    try:
                        if opened:
                            setattr(c.editor, 'tutorial_layers_view_opened_pulse', True)
                    except Exception:
                        pass
                    logger.debug(f"[DEBUG][Toolbar/Events] layers_view_open -> {opened}")
                    return True
                if tool_name == "add_zone":
                    # Delegate to the Add Zone tool controller, which enforces exclusivity
                    c.add_zone.toggle()
                    return True
                if tool_name == "delete_zone":
                    # Delegate to the Delete Zone tool controller, which enforces exclusivity
                    c.delete_zone.toggle()
                    return True
                if tool_name == "paint_tiles":
                    # Open/close the floating Tile Picker panel
                    try:
                        opened = c.paint_tiles.toggle()
                        # Ensure mutual exclusivity with other modes
                        editor.add_zone_mode = False
                        editor.delete_zone_mode = False
                        editor.clear_colliders_mode = False
                        editor.paint_colliders_mode = False
                        logger.debug(f"[DEBUG][Toolbar/Events] paint_tiles_open -> {opened}")
                    except Exception:
                        _toggle_pair("paint_tiles_mode", ["add_zone_mode", "delete_zone_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "clear_colliders":
                    c.clear_colliders.toggle()
                    return True
                if tool_name == "paint_colliders":
                    # Delegate to the Paint Colliders tool controller, which enforces exclusivity
                    c.paint_colliders.toggle()
                    return True
                if tool_name == "debug_coords":
                    # Toggle simple boolean flag in editor state; no exclusividad con otros modos.
                    try:
                        cur = bool(getattr(editor, "show_debug_overlay", False))
                        setattr(editor, "show_debug_overlay", not cur)
                        logger.debug("[DEBUG][Toolbar/Events] show_debug_overlay -> %s", not cur)
                    except Exception:
                        pass
                    return True

        # Dropdown de capas: delegar a ViewLayersEvents
        if editor.layers_view_open:
            if c.view_layers.events.handle_dropdown_click(mouse_pos):
                return True

        return False

