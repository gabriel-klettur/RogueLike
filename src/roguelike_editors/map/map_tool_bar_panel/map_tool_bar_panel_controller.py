import logging
from .map_tool_bar_panel_view import MapToolBarPanelView
from .map_tool_bar_panel_model import MapToolBarPanelModel
from .map_tool_bar_panel_events import MapToolBarPanelEvents
from .add_zone.add_zone_controller import AddZoneController
from .clear_colliders.clear_colliders_controller import ClearCollidersController
from .delete_zone.delete_zone_controller import DeleteZoneController
from .paint_colliders.paint_colliders_controller import PaintCollidersController
from .view_layers.view_layers_controller import ViewLayersController
from .paint_tiles.paint_tiles_controller import PaintTilesController

logger = logging.getLogger(__name__)


class MapToolBarPanelController:
    """
    Toolbar controller for the Map Editor, colocated under map_tool_bar_panel.
    Delegates rendering and dragging to MapToolBarPanelView/ToolbarView.
    Exposes the same API as the previous inline MapToolbarController.
    """

    def __init__(self, editor_state, map_controller=None):
        self.editor = editor_state
        # Optional back-reference to the MapEditorController for tool actions
        self.map_controller = map_controller
        # Will be injected by MapEditorManager to allow toolbar to toggle tutorial
        self.editor_manager = None

        # Model: geometry, icons, and rects
        self.model = MapToolBarPanelModel(editor_state, x=10, y=10, size=64, padding=8)
        # Mirror key attributes for legacy compatibility (used by view/layout code)
        self.x, self.y = self.model.x, self.model.y
        self.size, self.padding = self.model.size, self.model.padding
        self.icons = self.model.icons
        self.icon_rects = self.model.icon_rects

        # Events
        self.events = MapToolBarPanelEvents(self, self.model)

        # View wrapper that hosts the shared ToolbarView
        self.view = MapToolBarPanelView(self, self.model)

        # Tool controllers
        self.view_layers = ViewLayersController(
            editor_state=editor_state,
            toolbar_controller=self,
        )
        self.add_zone = AddZoneController(
            editor_state=editor_state,
            map_controller=self.map_controller,
            toolbar_controller=self,
        )
        self.delete_zone = DeleteZoneController(
            editor_state=editor_state,
            map_controller=self.map_controller,
            toolbar_controller=self,
        )
        # Paint tiles tool: opens floating Tile Picker
        self.paint_tiles = PaintTilesController(
            editor_state=editor_state,
            map_controller=self.map_controller,
            toolbar_controller=self,
        )
        self.clear_colliders = ClearCollidersController(
            editor_state=editor_state,
            map_controller=self.map_controller,
            toolbar_controller=self,
        )
        # Pintar colliders (nuevo MVC)
        self.paint_colliders = PaintCollidersController(
            editor_state=editor_state,
            map_controller=self.map_controller,
            toolbar_controller=self,
        )

    def handle_click(self, mouse_pos: tuple[int, int]) -> bool:
        """
        Left-click handler delegates to events module.
        """
        return bool(self.events.handle_click(mouse_pos))

    def is_active(self, tool: str) -> bool:
        """
        Indicates to ToolbarView whether a button should be rendered as active.
        """
        if tool == "map_tutorial":
            try:
                tm = getattr(self, 'editor_manager', None)
                return bool(tm and getattr(tm, 'tutorial', None) and tm.tutorial.is_active())
            except Exception:
                return False
        if tool == "view_layers":
            return bool(self.editor.layers_view_open)
        if tool == "add_zone":
            return bool(getattr(self.editor, "add_zone_mode", False))
        if tool == "delete_zone":
            return bool(getattr(self.editor, "delete_zone_mode", False))
        if tool == "paint_tiles":
            try:
                return bool(self.paint_tiles.is_open())
            except Exception:
                return bool(getattr(self.editor, "paint_tiles_mode", False))
        if tool == "clear_colliders":
            return bool(getattr(self.editor, "clear_colliders_mode", False))
        if tool == "paint_colliders":
            return bool(getattr(self.editor, "paint_colliders_mode", False))
        return False

    def _toggle_mode(self, mode_attr: str, disable: list[str] = []) -> None:
        """Toggle a mode in editor_state and disable others from the provided list."""
        self.model.toggle_mode(mode_attr, disable)

    # Dropdown selection is now handled by ViewLayers MVC

