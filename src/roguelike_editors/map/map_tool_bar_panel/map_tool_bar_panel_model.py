import logging
import pygame
from roguelike_engine.utils.loader import load_image

logger = logging.getLogger(__name__)


class MapToolBarPanelModel:
    """
    State and domain logic for the Map toolbar panel.
    Holds geometry, icons, rects, and editor mutations.
    """
    def __init__(self, editor_state, *, x: int = 10, y: int = 10, size: int = 64, padding: int = 8):
        self.editor = editor_state
        # Geometry/layout
        self.x = x
        self.y = y
        self.size = size
        self.padding = padding
        # Visual assets
        self.icons: dict[str, pygame.Surface] = self._load_icons()
        # Runtime rects
        self.icon_rects: dict[str, pygame.Rect] = {}

    # ---------------------------
    # Assets
    # ---------------------------
    def _load_icons(self) -> dict[str, pygame.Surface]:
        return {            
            "map_tutorial": load_image("assets/ui/tutorials_button.png", (self.size, self.size)),
            "add_zone": load_image("assets/ui/add_zone.png", (self.size, self.size)),
            "delete_zone": load_image("assets/ui/delete_zone.png", (self.size, self.size)),
            "paint_tiles": load_image("assets/ui/pintar_tiles_zone.png", (self.size, self.size)),
            "clear_colliders": load_image("assets/ui/vaciar_colliders_zone.png", (self.size, self.size)),
            "paint_colliders": load_image("assets/ui/pintar_colliders_zone.png", (self.size, self.size)),
            "view_layers": load_image("assets/ui/layers_view_tool.png", (self.size, self.size)),
        }

    # ---------------------------
    # Editor state mutations
    # ---------------------------
    def toggle_mode(self, mode_attr: str, disable: list[str] | tuple[str, ...] = ()) -> None:
        current = getattr(self.editor, mode_attr)
        setattr(self.editor, mode_attr, not current)
        for other in disable:
            setattr(self.editor, other, False)
