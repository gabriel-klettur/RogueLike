import logging
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer
from .map_tool_bar_panel_view import MapToolBarPanelView, TOOLS
# Optional imports for delegation; controller keeps fallbacks
try:
    from .map_tool_bar_panel_model import MapToolBarPanelModel  # type: ignore
except Exception:  # pragma: no cover - model may not exist yet
    MapToolBarPanelModel = None  # type: ignore
try:
    from .map_tool_bar_panel_events import MapToolBarPanelEvents  # type: ignore
except Exception:  # pragma: no cover - events may not exist yet
    MapToolBarPanelEvents = None  # type: ignore

logger = logging.getLogger(__name__)


class MapToolBarPanelController:
    """
    Toolbar controller for the Map Editor, colocated under map_tool_bar_panel.
    Delegates rendering and dragging to MapToolBarPanelView/ToolbarView.
    Exposes the same API as the previous inline MapToolbarController.
    """

    def __init__(self, editor_state):
        self.editor = editor_state

        # Model: preferred place for geometry, icons, and rects
        if MapToolBarPanelModel is not None:
            self.model = MapToolBarPanelModel(editor_state, x=10, y=10, size=64, padding=8)
            # Mirror key attributes for legacy compatibility
            self.x, self.y = self.model.x, self.model.y
            self.size, self.padding = self.model.size, self.model.padding
            self.icons = self.model.icons
            self.icon_rects = self.model.icon_rects
            self.option_rects = self.model.option_rects
        else:
            # Legacy inline fields (fallback if model is not available)
            self.model = None
            self.x, self.y = 10, 10
            self.size = 64
            self.padding = 8
            self.icons: dict[str, pygame.Surface] = self._load_icons()
            self.icon_rects: dict[str, pygame.Rect] = {}
            self.option_rects: dict[Layer | str, pygame.Rect] = {}

        # Events: delegate if available
        self.events = MapToolBarPanelEvents(self, self.model) if MapToolBarPanelEvents is not None else None

        # View wrapper that hosts the shared ToolbarView
        self.view = MapToolBarPanelView(self, getattr(self, "model", None))

    def handle_click(self, mouse_pos: tuple[int, int]) -> bool:
        """
        Handle left-clicks using rects provided by ToolbarView (icon_rects).
        Includes a pre-render fallback to compute rects from widget geometry.
        """
        # Delegate to events module if present
        if self.events is not None and hasattr(self.events, "handle_click"):
            try:
                return bool(self.events.handle_click(mouse_pos))
            except Exception:
                # Fallback to legacy path if delegation fails
                pass
        # Ensure icon_rects even before first render
        if not self.icon_rects:
            widget = getattr(getattr(self, 'view', None), 'widget', None)
            if widget and getattr(widget, 'icon_rects', None):
                self.icon_rects = dict(widget.icon_rects)
            elif widget:
                # Precompute rects based on ToolbarView geometry
                edge = getattr(widget, 'edge_padding', 8)
                panel_pos = widget.panel.pos or (widget.x, widget.y)
                size, pad = widget.size, widget.padding
                self.icon_rects = {}
                for idx, tool_name in enumerate(TOOLS):
                    local = pygame.Rect(edge, edge + idx * (size + pad), size, size)
                    self.icon_rects[tool_name] = local.move(panel_pos)

        # Map handlers per tool
        def _toggle_pair(primary: str, disable: list[str]):
            self._toggle_mode(primary, disable=disable)
            logger.debug(f"[DEBUG][Toolbar] {primary} -> {getattr(self.editor, primary)}")

        for tool_name, rect in self.icon_rects.items():
            if rect and rect.collidepoint(mouse_pos):
                if tool_name == "view_layers":
                    self.editor.layers_view_open = not self.editor.layers_view_open
                    logger.debug(f"[DEBUG][Toolbar] layers_view_open -> {self.editor.layers_view_open}")
                    return True
                if tool_name == "add_zone":
                    _toggle_pair("add_zone_mode", ["delete_zone_mode", "paint_tiles_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "delete_zone":
                    _toggle_pair("delete_zone_mode", ["add_zone_mode", "paint_tiles_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "paint_tiles":
                    _toggle_pair("paint_tiles_mode", ["add_zone_mode", "delete_zone_mode", "clear_colliders_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "clear_colliders":
                    _toggle_pair("clear_colliders_mode", ["add_zone_mode", "delete_zone_mode", "paint_tiles_mode", "paint_colliders_mode"])
                    return True
                if tool_name == "paint_colliders":
                    _toggle_pair("paint_colliders_mode", ["add_zone_mode", "delete_zone_mode", "paint_tiles_mode", "clear_colliders_mode"])
                    return True

        # Layers dropdown
        if self.editor.layers_view_open:
            for key, rect in self.option_rects.items():
                if rect and rect.collidepoint(mouse_pos):
                    self._handle_dropdown_selection(key)
                    return True

        return False

    def is_active(self, tool: str) -> bool:
        """
        Indicates to ToolbarView whether a button should be rendered as active.
        """
        if tool == "view_layers":
            return bool(self.editor.layers_view_open)
        if tool == "add_zone":
            return bool(getattr(self.editor, "add_zone_mode", False))
        if tool == "delete_zone":
            return bool(getattr(self.editor, "delete_zone_mode", False))
        if tool == "paint_tiles":
            return bool(getattr(self.editor, "paint_tiles_mode", False))
        if tool == "clear_colliders":
            return bool(getattr(self.editor, "clear_colliders_mode", False))
        if tool == "paint_colliders":
            return bool(getattr(self.editor, "paint_colliders_mode", False))
        return False

    def _load_icons(self) -> dict[str, pygame.Surface]:
        """Load and scale icons for the map editor toolbar."""
        return {
            "view_layers": load_image("assets/ui/layers_view_tool.png", (self.size, self.size)),
            "add_zone": load_image("assets/ui/add_zone.png", (self.size, self.size)),
            "delete_zone": load_image("assets/ui/delete_zone.png", (self.size, self.size)),
            "paint_tiles": load_image("assets/ui/pintar_tiles_zone.png", (self.size, self.size)),
            "clear_colliders": load_image("assets/ui/vaciar_colliders_zone.png", (self.size, self.size)),
            "paint_colliders": load_image("assets/ui/pintar_colliders_zone.png", (self.size, self.size)),
        }

    def _toggle_mode(self, mode_attr: str, disable: list[str] = []) -> None:
        """
        Toggle a mode in editor_state and disable others from the provided list.
        Delegates to model if present.
        """
        if getattr(self, "model", None) is not None and hasattr(self.model, "toggle_mode"):
            self.model.toggle_mode(mode_attr, disable)
        else:
            current = getattr(self.editor, mode_attr)
            setattr(self.editor, mode_attr, not current)
            for other in disable:
                setattr(self.editor, other, False)

    def _handle_dropdown_selection(self, key: Layer | str) -> None:
        """
        Delegate to model if available; otherwise mirror legacy behavior using
        editor.visible_layers and flags.
        """
        if getattr(self, "model", None) is not None and hasattr(self.model, "handle_dropdown_selection"):
            self.model.handle_dropdown_selection(key)
            return
        if key == "show_all":
            for layer in self.editor.visible_layers:
                self.editor.visible_layers[layer] = True
            self.editor.show_buildings = True
            logger.debug("[DEBUG][Layer View] show_all: all layers visible")
            return
        elif key == "hide_all":
            for layer in self.editor.visible_layers:
                self.editor.visible_layers[layer] = False
            self.editor.show_buildings = False
            logger.debug("[DEBUG][Layer View] hide_all: all layers hidden")
            return
        elif isinstance(key, Layer):
            vl = self.editor.visible_layers
            vl[key] = not vl[key]
            logger.debug(f"[DEBUG][Layer View] {key.name}: {'visible' if vl[key] else 'hidden'}")
            return
        elif key == "buildings":
            self.editor.show_buildings = not self.editor.show_buildings
            logger.debug(f"[DEBUG][Layer View] buildings: {'visible' if self.editor.show_buildings else 'hidden'}")
            return
        elif key == "colliders":
            self.editor.show_colliders = not self.editor.show_colliders
            logger.debug(f"[DEBUG][Layer View] colliders: {'visible' if self.editor.show_colliders else 'hidden'}")
            return

