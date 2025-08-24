import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE

class SpatialIndex:
    def __init__(self, map_manager, buildings):
        """
        Indexa estáticamente los tiles del mapa y guarda la lista de edificios.
        """
        # Static map tile index
        self._map_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for tile in getattr(map_manager, 'solid_tiles', []):
            # Index map rects in all grid cells they overlap (robust even if off-grid)
            r = tile.rect
            x1 = r.left // TILE_SIZE
            y1 = r.top // TILE_SIZE
            x2 = (r.right - 1) // TILE_SIZE
            y2 = (r.bottom - 1) // TILE_SIZE
            for gx in range(x1, x2 + 1):
                for gy in range(y1, y2 + 1):
                    self._map_index.setdefault((gx, gy), []).append(r)
        # Static building index (cache para colisiones dinámicas)
        self._building_index: dict[tuple[int,int], list[pygame.Rect]] = {}
        for b in buildings:
            for tile_rect in b.collision_tiles:
                # Index building tile rects across all overlapped cells
                x1 = tile_rect.left // TILE_SIZE
                y1 = tile_rect.top // TILE_SIZE
                x2 = (tile_rect.right - 1) // TILE_SIZE
                y2 = (tile_rect.bottom - 1) // TILE_SIZE
                for gx in range(x1, x2 + 1):
                    for gy in range(y1, y2 + 1):
                        self._building_index.setdefault((gx, gy), []).append(tile_rect)
        # Guardar referencia a edificios para dinámico
        self.buildings = buildings

    def get_solid_tiles_for_rect(self, rect: pygame.Rect) -> list[pygame.Rect]:
        """
        Devuelve todos los rects sólidos (mapa y edificios) que cubre el rect dado.
        """
        x1, y1 = rect.left // TILE_SIZE, rect.top // TILE_SIZE
        # Use right/bottom - 1 to avoid including an extra cell when exactly on boundary
        x2, y2 = (rect.right - 1) // TILE_SIZE, (rect.bottom - 1) // TILE_SIZE
        result: list[pygame.Rect] = []
        # Consultar colisiones de mapa y edificios (índices cacheados)
        for x in range(x1, x2 + 1):
            for y in range(y1, y2 + 1):
                result.extend(self._map_index.get((x, y), []))
                result.extend(self._building_index.get((x, y), []))
        # Quitar duplicados manteniendo el orden usando id del objeto Rect
        dedup: list[pygame.Rect] = []
        seen_ids: set[int] = set()
        for r in result:
            rid = id(r)
            if rid not in seen_ids:
                seen_ids.add(rid)
                dedup.append(r)
        return dedup