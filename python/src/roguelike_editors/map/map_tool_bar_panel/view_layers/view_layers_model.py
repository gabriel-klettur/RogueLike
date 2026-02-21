import logging
import pygame
from roguelike_engine.map.model.layer import Layer

logger = logging.getLogger(__name__)


class ViewLayersModel:
    """
    Modelo para el botón 'view_layers'.
    - Mantiene los rectángulos de opciones del dropdown.
    - Aplica selecciones sobre la visibilidad de capas y flags del editor.
    """
    def __init__(self, editor_state):
        self.editor = editor_state
        # Rectángulos clicables del dropdown (clave -> pygame.Rect)
        self.option_rects: dict[Layer | str, pygame.Rect] = {}

    # ---------------------------
    # Mutaciones de estado
    # ---------------------------
    def toggle_open(self) -> bool:
        """Alterna la apertura del dropdown de capas."""
        self.editor.layers_view_open = not bool(self.editor.layers_view_open)
        logger.debug("[ViewLayersModel] layers_view_open -> %s", self.editor.layers_view_open)
        return self.editor.layers_view_open

    def close(self) -> None:
        self.editor.layers_view_open = False

    def apply_selection(self, key: Layer | str) -> None:
        """
        Aplica la acción asociada a 'key' sobre la visibilidad del editor.
        Claves válidas: "show_all", "hide_all", cada Layer, "buildings", "colliders".
        """
        e = self.editor
        if key == "show_all":
            for layer in e.visible_layers:
                e.visible_layers[layer] = True
            e.show_buildings = True
            logger.debug("[ViewLayers] show_all: all layers visible")
            return
        if key == "hide_all":
            for layer in e.visible_layers:
                e.visible_layers[layer] = False
            e.show_buildings = False
            logger.debug("[ViewLayers] hide_all: all layers hidden")
            return
        if isinstance(key, Layer):
            vl = e.visible_layers
            vl[key] = not vl[key]
            logger.debug("[ViewLayers] %s -> %s", key.name, vl[key])
            return
        if key == "buildings":
            e.show_buildings = not e.show_buildings
            logger.debug("[ViewLayers] buildings -> %s", e.show_buildings)
            return
        if key == "colliders":
            e.show_colliders = not e.show_colliders
            logger.debug("[ViewLayers] colliders -> %s", e.show_colliders)
            return

