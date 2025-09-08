import pygame
import math
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.map_model import Map as MapModel
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.loader import get_sprite_for_tile

import logging
logger = logging.getLogger(__name__)

# Cache scaled sprites per (sprite, zoom)
_SCALED_CACHE: dict[tuple[pygame.Surface, float], pygame.Surface] = {}

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
        logger.debug(f" _build_chunk_surfaces called for zoom {zoom}")
        """
        Pre-dibuja cada chunk (bloque de tiles) en una surface escalada
        y la guarda en self.chunks_by_zoom[zoom].
        """
        width  = len(map_model.matrix[0])
        height = len(map_model.matrix)
        cs = self.chunk_size

        n_chunks_x = math.ceil(width  / cs)
        n_chunks_y = math.ceil(height / cs)
        chunk_dict: dict[tuple[int,int], pygame.Surface] = {}

        # Precompute sprites for each unique (char, overlay) pair with bounds check
        sprite_map: dict[tuple[str, str|None], pygame.Surface|None] = {}
        matrix = map_model.matrix
        height_m = len(matrix)
        for layer, grid in map_model.layers.items():
            for y, row in enumerate(grid):
                if y >= height_m:
                    continue
                row_str = matrix[y]
                width_m = len(row_str)
                for x, code in enumerate(row):
                    if x >= width_m:
                        continue
                    char = row_str[x]
                    key = (char, code)
                    if key not in sprite_map:
                        sprite_map[key] = get_sprite_for_tile(char, code)

        # Debug: imprimir claves sin sprite en sprite_map
        missing = [k for k, s in sprite_map.items() if s is None]
        logger.debug(f" claves sin sprite: {missing}")
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        for cy in range(n_chunks_y):
            for cx in range(n_chunks_x):
                # tamaño en tiles de este chunk (puede recortarse al borde)
                tile_w = min(cs, width  - cx*cs)
                tile_h = min(cs, height - cy*cs)

                # tamaño en píxeles tras escalar (usar redondeo para evitar acumulación de truncado)
                pix_w = int(round(tile_w * TILE_SIZE * zoom))
                pix_h = int(round(tile_h * TILE_SIZE * zoom))

                surf = pygame.Surface((pix_w, pix_h), pygame.SRCALPHA)
                zkey = float(zoom)

                # dibujar cada tile por capa en orden usando raw_layers
                for ty in range(cy*cs, cy*cs + tile_h):
                    for tx in range(cx*cs, cx*cs + tile_w):
                        char = map_model.matrix[ty][tx]
                        for layer in layers_ordered:
                            code = map_model.layers[layer][ty][tx]
                            # Skip fallback drawing for blank overlay on non-Ground layers
                            if not code and layer != Layer.Ground:
                                continue
                            sprite = sprite_map.get((char, code))
                            if sprite is None:
                                logger.debug(f" sin sprite para tile ({ty},{tx}) char={char}, code={code}")
                            if not sprite:
                                continue
                            # scaled cache
                            skey = (sprite, zkey)
                            scaled = _SCALED_CACHE.get(skey)
                            if scaled is None:
                                sw, sh = sprite.get_size()
                                scaled = pygame.transform.scale(sprite, (int(round(sw * zoom)), int(round(sh * zoom))))
                                _SCALED_CACHE[skey] = scaled
                            # posición dentro del chunk
                            px = int(round((tx - cx*cs) * TILE_SIZE * zoom))
                            py = int(round((ty - cy*cs) * TILE_SIZE * zoom))
                            surf.blit(scaled, (px, py))

                chunk_dict[(cx, cy)] = surf

        self.chunks_by_zoom[zoom] = chunk_dict

    def invalidate_cache(self):
        logger.debug(f" invalidate_cache called")
        """Forzar reconstrucción de todos los chunks en el próximo render."""
        self.chunks_by_zoom.clear()

    def update_chunks(self, map_model, camera, cells):
        """
        Rebuild only the chunks containing the given tile coordinates.
        """
        # Determine zoom level (exact)
        zoom = max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1)

        # Ensure base cache exists
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)
        cs = self.chunk_size
        # Precompute sprite mapping for this zoom and map
        sprite_map: dict[tuple[str, str|None], pygame.Surface|None] = {}
        matrix = map_model.matrix
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        # Build sprite map
        for layer, grid in map_model.layers.items():
            for y, row in enumerate(grid):
                if y >= len(matrix):
                    continue
                row_str = matrix[y]
                for x, code in enumerate(row):
                    if x >= len(row_str):
                        continue
                    key = (row_str[x], code)
                    if key not in sprite_map:                        
                        sprite_map[key] = get_sprite_for_tile(row_str[x], code)
        # Rebuild each affected chunk
        for row, col in set(cells):
            cx = col // cs
            cy = row // cs
            width = len(matrix[0]) if matrix else 0
            height = len(matrix)
            tile_w = min(cs, width - cx*cs)
            tile_h = min(cs, height - cy*cs)
            pix_w = int(round(tile_w * TILE_SIZE * zoom))
            pix_h = int(round(tile_h * TILE_SIZE * zoom))

            surf = pygame.Surface((pix_w, pix_h), pygame.SRCALPHA)
            for ty in range(cy*cs, cy*cs + tile_h):
                for tx in range(cx*cs, cx*cs + tile_w):
                    char = matrix[ty][tx]
                    for layer in layers_ordered:
                        code = map_model.layers[layer][ty][tx]
                        if not code and layer != Layer.Ground:
                            continue
                        sprite = sprite_map.get((char, code))
                        if not sprite:
                            continue
                        skey = (sprite, zoom)
                        scaled = _SCALED_CACHE.get(skey)
                        if scaled is None:
                            sw, sh = sprite.get_size()
                            scaled = pygame.transform.scale(sprite, (int(round(sw * zoom)), int(round(sh * zoom))))
                            _SCALED_CACHE[skey] = scaled
                        px = int(round((tx - cx*cs) * TILE_SIZE * zoom))
                        py = int(round((ty - cy*cs) * TILE_SIZE * zoom))

                        surf.blit(scaled, (px, py))
            # Store updated chunk
            self.chunks_by_zoom[zoom][(cx, cy)] = surf

    def render(
        self,
        screen: pygame.Surface,
        camera,
        map_model: MapModel
    ) -> list[pygame.Rect]:
        #logger.debug(f" render called for zoom {round(camera.zoom*10)/10.0}, cache keys: {list(self.chunks_by_zoom.keys())}")
        """
        Dibuja únicamente los chunks visibles según la cámara,
        devolviendo la lista de dirty rects.
        """
        dirty_rects: list[pygame.Rect] = []
        screen_w, screen_h = screen.get_size()
        # Use exact zoom and clamp to minimum to avoid division by zero
        zoom = max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1)

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
                     math.ceil(len(map_model.matrix[0]) / cs))
        min_cy = max(int((top   // TILE_SIZE) // cs), 0)
        max_cy = min(int((bottom// TILE_SIZE) // cs) + 1,
                     math.ceil(len(map_model.matrix) / cs))

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