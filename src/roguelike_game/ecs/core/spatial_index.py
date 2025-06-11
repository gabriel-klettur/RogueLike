# Path: src/roguelike_game/ecs/core/spatial_index.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class SpatialIndex:
    def __init__(self, map_manager, buildings):
        """
        Indexa estáticamente los tiles del mapa y guarda la lista de edificios.
        """
        # Static map tile index
        self._map_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for tile in map_manager.solid_tiles:
            key = (tile.rect.x // TILE_SIZE, tile.rect.y // TILE_SIZE)
            self._map_index.setdefault(key, []).append(tile.rect)
        # Static building index (cache para colisiones dinámicas)
        self._building_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for b in buildings:
            for tile_rect in b.collision_tiles:
                cell = (tile_rect.x // TILE_SIZE, tile_rect.y // TILE_SIZE)
                self._building_index.setdefault(cell, []).append(tile_rect)
        # Guardar referencia a edificios para dinámico
        self.buildings = buildings

    def get_solid_tiles_for_rect(self, rect: pygame.Rect) -> list[pygame.Rect]:
        """
        Devuelve todos los rects sólidos (mapa y edificios) que cubre el rect dado.
        """
        x1, y1 = rect.left // TILE_SIZE, rect.top // TILE_SIZE
        x2, y2 = rect.right // TILE_SIZE, rect.bottom // TILE_SIZE
        result: list[pygame.Rect] = []
        # Consultar colisiones de mapa y edificios (índices cacheados)
        for x in range(x1, x2 + 1):
            for y in range(y1, y2 + 1):
                result.extend(self._map_index.get((x, y), []))
                result.extend(self._building_index.get((x, y), []))
        return result
