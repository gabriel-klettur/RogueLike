# src/roguelike_engine/tile/view/tile_view.py

import pygame
from roguelike_engine.tile.model.tile import Tile

class TileView:
    """
    Sabe dibujar un único Tile en pantalla usando la cámara.
    """
    def __init__(
        self,
        screen: pygame.Surface,
        camera
    ):
        self.screen = screen
        self.camera = camera

    def render(self, tile: Tile) -> pygame.Rect:
        """
        Dibuja el sprite del tile y devuelve el rect en pantalla.
        """
        screen_pos = self.camera.apply((tile.x, tile.y))
        return self.screen.blit(tile.sprite, screen_pos)
