# src/roguelike_engine/map/view/chunked_map_view.py

import pygame
import math
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.map_model import Map as MapModel

class ChunkedMapView:
    """
    Vista optimizada para renderizar mapas por chunks con cache de superficies
    escaladas según el zoom.
    """
    def __init__(self, chunk_size: int = 32):
        # número de tiles por chunk (chunk_size x chunk_size)
        self.chunk_size = chunk_size
        # cache: { zoom: { (chunk_x,chunk_y): Surface } }
        self.chunks_by_zoom: dict[float, dict[tuple[int,int], pygame.Surface]] = {}

    def _build_chunk_surfaces(self, map_model: MapModel, zoom: float):
        """
        Pre-dibuja cada chunk (bloque de tiles) en una surface escalada
        y la guarda en self.chunks_by_zoom[zoom].
        """
        width  = len(map_model.tiles[0])
        height = len(map_model.tiles)
        cs = self.chunk_size

        n_chunks_x = math.ceil(width  / cs)
        n_chunks_y = math.ceil(height / cs)
        chunk_dict: dict[tuple[int,int], pygame.Surface] = {}

        for cy in range(n_chunks_y):
            for cx in range(n_chunks_x):
                # tamaño en tiles de este chunk (puede recortarse al borde)
                tile_w = min(cs, width  - cx*cs)
                tile_h = min(cs, height - cy*cs)

                # tamaño en píxeles tras escalar
                pix_w = int(tile_w * TILE_SIZE * zoom)
                pix_h = int(tile_h * TILE_SIZE * zoom)

                surf = pygame.Surface((pix_w, pix_h), pygame.SRCALPHA)
                zkey = round(zoom * 10) / 10.0

                # dibujar cada tile del chunk en la surface
                for ty in range(cy*cs, cy*cs + tile_h):
                    for tx in range(cx*cs, cx*cs + tile_w):
                        tile = map_model.tiles[ty][tx]

                        # sprite cacheado por zoom
                        sprite = tile.scaled_cache.get(zkey)
                        if sprite is None:
                            sw, sh = tile.sprite.get_size()
                            sprite = pygame.transform.scale(
                                tile.sprite,
                                (int(sw * zoom), int(sh * zoom))
                            )
                            tile.scaled_cache[zkey] = sprite

                        # posición dentro del chunk
                        x = int((tx - cx*cs) * TILE_SIZE * zoom)
                        y = int((ty - cy*cs) * TILE_SIZE * zoom)
                        surf.blit(sprite, (x, y))

                chunk_dict[(cx, cy)] = surf

        self.chunks_by_zoom[zoom] = chunk_dict

    def invalidate_cache(self):
        """Forzar reconstrucción de todos los chunks en el próximo render."""
        self.chunks_by_zoom.clear()

    def render(
        self,
        screen: pygame.Surface,
        camera,
        map_model: MapModel
    ) -> list[pygame.Rect]:
        """
        Dibuja únicamente los chunks visibles según la cámara,
        devolviendo la lista de dirty rects.
        """
        dirty_rects: list[pygame.Rect] = []
        screen_w, screen_h = screen.get_size()
        zoom = round(camera.zoom * 10) / 10.0

        # rebuild cache para este zoom si falta
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)

        chunks = self.chunks_by_zoom[zoom]
        cs = self.chunk_size

        # límites en coordenadas de mundo
        left   = camera.offset_x
        top    = camera.offset_y
        right  = camera.offset_x + screen_w  / zoom
        bottom = camera.offset_y + screen_h / zoom

        # índices de chunk visibles
        min_cx = max(int((left  // TILE_SIZE) // cs), 0)
        max_cx = min(int((right // TILE_SIZE) // cs) + 1,
                     math.ceil(len(map_model.tiles[0]) / cs))
        min_cy = max(int((top   // TILE_SIZE) // cs), 0)
        max_cy = min(int((bottom// TILE_SIZE) // cs) + 1,
                     math.ceil(len(map_model.tiles) / cs))

        # blitear sólo los chunks en ese rango
        for cy in range(min_cy, max_cy):
            for cx in range(min_cx, max_cx):
                surf = chunks.get((cx, cy))
                if surf is None:
                    continue
                world_x = cx * cs * TILE_SIZE
                world_y = cy * cs * TILE_SIZE
                screen_x, screen_y = camera.apply((world_x, world_y))
                rect = screen.blit(surf, (screen_x, screen_y))
                dirty_rects.append(rect)

        return dirty_rects
