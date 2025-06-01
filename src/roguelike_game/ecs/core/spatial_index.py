# Path: src/roguelike_game/ecs/core/spatial_index.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class SpatialIndex:
    def __init__(self, map_manager, buildings):
        """
        Construye el índice espacial a partir de todos los tiles sólidos del MapManager
        y de los rects de colisión de los edificios.
        """
        self._index: dict[tuple[int,int], list[pygame.Rect]] = {}
        # Indexar tiles sólidos del mapa
        for tile in map_manager.solid_tiles:
            key = (tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE)
            self._index.setdefault(key, []).append(tile.rect)
        # Indexar colisión de edificios
        for b in buildings:
            for rect in b.collision_tiles:
                key = (rect.x // TILE_SIZE, rect.y // TILE_SIZE)
                self._index.setdefault(key, []).append(rect)

        # Bandera para saber si ya indexamos edificios (útil para llamadas posteriores)
        self._buildings_indexed = True

    def get_solid_tiles_for_rect(self, rect: pygame.Rect) -> list[pygame.Rect]:
        """
        Dado un pygame.Rect, devuelve la lista de todos los rects sólidos
        que intersectan las celdas cubiertas por ese rect.
        """
        x1, y1 = rect.left // TILE_SIZE, rect.top // TILE_SIZE
        x2, y2 = rect.right // TILE_SIZE, rect.bottom // TILE_SIZE

        resultado = []
        for x in range(x1, x2 + 1):
            for y in range(y1, y2 + 1):
                resultado.extend(self._index.get((x, y), []))
        return resultado
