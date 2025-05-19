# src/roguelike_engine/map/view/map_view.py

import pygame
from roguelike_engine.tile.view.tile_view import TileView
from roguelike_engine.map.model.map_model import Map as MapModel

class MapView:
    """
    Vista básica para renderizar todos los tiles de un MapModel,
    delegando el dibujo de cada celda a TileView.
    """
    def __init__(self):
        # TileView se inicializa en el primer render
        self.tile_view: TileView | None = None

    def render(
        self,
        screen: pygame.Surface,
        camera,
        map_model: MapModel
    ) -> list[pygame.Rect]:
        """
        Dibuja todos los tiles de map_model.tiles y devuelve la lista
        de rects sucios (dirty rects) para actualización parcial.
        """
        if self.tile_view is None:
            self.tile_view = TileView(screen, camera)

        dirty_rects: list[pygame.Rect] = []
        for row in map_model.tiles:
            for tile in row:
                rect = self.tile_view.render(tile)
                dirty_rects.append(rect)
        return dirty_rects
