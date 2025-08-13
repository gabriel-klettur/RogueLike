import logging
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.map.model.layer import Layer

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
        self.option_rects: dict[Layer | str, pygame.Rect] = {}

    # ---------------------------
    # Assets
    # ---------------------------
    def _load_icons(self) -> dict[str, pygame.Surface]:
        return {
            "view_layers": load_image("assets/ui/layers_view_tool.png", (self.size, self.size)),
            "add_zone": load_image("assets/ui/add_zone.png", (self.size, self.size)),
            "delete_zone": load_image("assets/ui/delete_zone.png", (self.size, self.size)),
            "paint_tiles": load_image("assets/ui/pintar_tiles_zone.png", (self.size, self.size)),
            "clear_colliders": load_image("assets/ui/vaciar_colliders_zone.png", (self.size, self.size)),
            "paint_colliders": load_image("assets/ui/pintar_colliders_zone.png", (self.size, self.size)),
        }

    # ---------------------------
    # Editor state mutations
    # ---------------------------
    def toggle_mode(self, mode_attr: str, disable: list[str] | tuple[str, ...] = ()) -> None:
        current = getattr(self.editor, mode_attr)
        setattr(self.editor, mode_attr, not current)
        for other in disable:
            setattr(self.editor, other, False)

    def handle_dropdown_selection(self, key: Layer | str) -> None:
        """Mirror legacy behavior using editor.visible_layers and flags."""
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
