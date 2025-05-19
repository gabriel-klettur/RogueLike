# src/roguelike_engine/tile/controller/tile_controller.py

import pygame
from roguelike_engine.tile.model.tile import Tile

class TileController:
    """
    Controlador para interacción con tiles.
    (Opcional: si no hay interacción, puede omitirse.)
    """
    def __init__(self):
        self.selected: Tile | None = None

    def handle_event(
        self,
        event: pygame.event.Event,
        tiles: list[Tile]
    ):
        """
        Marca un tile como seleccionado al hacer click sobre él.
        """
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            for tile in tiles:
                if tile.rect.collidepoint(event.pos):
                    self.selected = tile
                    break
