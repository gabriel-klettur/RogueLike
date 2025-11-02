import pygame
import math
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.map_model import Map as MapModel
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.tile.utils.loader import get_sprite_for_tile
from roguelike_engine.config.map_config import global_map_settings

import logging
import math as _math
logger = logging.getLogger(__name__)
# Disable very chatty map chunk build logs by default
DEBUG_CHUNKED: bool = False
MAX_ZOOM: float = 10.0
MAX_SURFACE_DIM: int = 4096

# Cache scaled sprites per (sprite_id, zoom)
_SCALED_CACHE: dict[tuple[int, float], pygame.Surface] = {}

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
        if DEBUG_CHUNKED:
            logger.debug(f" _build_chunk_surfaces called for zoom {zoom}")

        """
        Pre-dibuja cada chunk (bloque de tiles) en una surface escalada
        y la guarda en self.chunks_by_zoom[zoom].
        """
        # Clamp zoom locally for safety
        try:
            z = float(zoom)
        except Exception:
            z = 1.0
        if not _math.isfinite(z):
            z = 1.0
        zoom = min(max(z or 1.0, 0.1), MAX_ZOOM)
        width  = len(map_model.matrix[0])
        height = len(map_model.matrix)
        cs = self.chunk_size

        n_chunks_x = math.ceil(width  / cs)
        n_chunks_y = math.ceil(height / cs)
        chunk_dict: dict[tuple[int,int], pygame.Surface] = {}

        # Precompute overlay policy once (avoid expensive checks in inner loops)
        try:
            use_ov = bool(getattr(global_map_settings, 'use_zones_json', False))
            if use_ov:
                from pathlib import Path as _P
                odir = getattr(global_map_settings, 'overlays_dir', None)
                has_ov = bool(odir and len(list(_P(odir).glob('*.overlay.json'))) > 0)
                offsets = getattr(global_map_settings, 'zone_offsets', {})
                user_keys = [k for k in offsets.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                overlay_blank_world = (not has_ov) and (len(user_keys) == 0)
            else:
                overlay_blank_world = False
        except Exception:
            overlay_blank_world = False

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

        # Debug opcional: imprimir claves sin sprite en sprite_map
        if DEBUG_CHUNKED:
            missing = [k for k, s in sprite_map.items() if s is None]
            logger.debug(f" claves sin sprite: {missing}")

        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        for cy in range(n_chunks_y):
            for cx in range(n_chunks_x):
                # tamaño en tiles de este chunk (puede recortarse al borde)
                tile_w = min(cs, width  - cx*cs)
                tile_h = min(cs, height - cy*cs)

                # tamaño en píxeles tras escalar (usar redondeo y tamaño entre 1..MAX_SURFACE_DIM)
                pix_w = min(MAX_SURFACE_DIM, max(1, int(round(tile_w * TILE_SIZE * zoom))))
                pix_h = min(MAX_SURFACE_DIM, max(1, int(round(tile_h * TILE_SIZE * zoom))))

                try:
                    surf = pygame.Surface((pix_w, pix_h), pygame.SRCALPHA)
                except Exception:
                    # Fallback ultra defensivo
                    surf = pygame.Surface((1, 1), pygame.SRCALPHA)
                # Fondo negro opaco para limpiar el contenido previo cuando no hay sprites
                try:
                    surf.fill((0, 0, 0, 255))
                except Exception:
                    pass

                zkey = float(zoom)

                # dibujar cada tile por capa en orden usando raw_layers
                for ty in range(cy*cs, cy*cs + tile_h):
                    for tx in range(cx*cs, cx*cs + tile_w):
                        char = map_model.matrix[ty][tx]
                        for layer in layers_ordered:
                            code = map_model.layers[layer][ty][tx]
                            # Draw policy: if it's a truly blank overlays-world, skip empty codes; else allow Ground fallback
                            if not code:
                                if overlay_blank_world:
                                    continue
                                if layer != Layer.Ground:
                                    continue
                            sprite = sprite_map.get((char, code))
                            if sprite is None and DEBUG_CHUNKED:
                                logger.debug(f" sin sprite para tile ({ty},{tx}) char={char}, code={code}")

                            if not sprite:
                                continue
                            # scaled cache (by sprite id and zoom)
                            skey = (id(sprite), zkey)
                            scaled = _SCALED_CACHE.get(skey)
                            if scaled is None:
                                sw, sh = sprite.get_size()
                                tw = min(MAX_SURFACE_DIM, max(1, int(round(sw * zoom))))
                                th = min(MAX_SURFACE_DIM, max(1, int(round(sh * zoom))))
                                try:
                                    scaled = pygame.transform.scale(sprite, (tw, th))
                                except Exception:
                                    scaled = sprite
                                _SCALED_CACHE[skey] = scaled
                            # posición dentro del chunk
                            px = int(round((tx - cx*cs) * TILE_SIZE * zoom))
                            py = int(round((ty - cy*cs) * TILE_SIZE * zoom))
                            try:
                                surf.blit(scaled, (px, py))
                            except Exception:
                                pass

                chunk_dict[(cx, cy)] = surf

        self.chunks_by_zoom[zoom] = chunk_dict

    def invalidate_cache(self):
        if DEBUG_CHUNKED:
            logger.debug(f" invalidate_cache called")

        """Forzar reconstrucción de todos los chunks en el próximo render."""
        self.chunks_by_zoom.clear()
        # Limpiar caché global de sprites escalados para evitar artefactos entre mundos/zooms
        try:
            _SCALED_CACHE.clear()
        except Exception:
            pass

    def update_chunks(self, map_model, camera, cells):
        """
        Rebuild only the chunks containing the given tile coordinates.
        """
        # Determine zoom level (clamped)
        zoom = min(max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1), MAX_ZOOM)

        # Ensure base cache exists
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)
        # Precompute overlay policy once (avoid expensive checks in inner loops)
        try:
            use_ov = bool(getattr(global_map_settings, 'use_zones_json', False))
            if use_ov:
                from pathlib import Path as _P
                odir = getattr(global_map_settings, 'overlays_dir', None)
                has_ov = bool(odir and len(list(_P(odir).glob('*.overlay.json'))) > 0)
                offsets = getattr(global_map_settings, 'zone_offsets', {})
                user_keys = [k for k in offsets.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                overlay_blank_world = (not has_ov) and (len(user_keys) == 0)
            else:
                overlay_blank_world = False
        except Exception:
            overlay_blank_world = False
        cs = self.chunk_size
        # Use a local lazy sprite cache only for tiles encountered in the chunks
        matrix = map_model.matrix
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        local_sprite_cache: dict[tuple[str, str|None], pygame.Surface|None] = {}
        # Compute unique chunks to rebuild (coalesce many cells per chunk)
        dirty_chunks: set[tuple[int,int]] = set()
        for row, col in set(cells):
            dirty_chunks.add((col // cs, row // cs))
        # Rebuild each affected chunk only once
        for (cx, cy) in dirty_chunks:
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
                        if not code:
                            if overlay_blank_world:
                                continue
                            if layer != Layer.Ground:
                                continue
                        key = (char, code)
                        sprite = local_sprite_cache.get(key)
                        if sprite is None and key not in local_sprite_cache:
                            sprite = get_sprite_for_tile(char, code)
                            local_sprite_cache[key] = sprite
                        if not sprite:
                            continue
                        skey = (id(sprite), zoom)
                        scaled = _SCALED_CACHE.get(skey)
                        if scaled is None:
                            sw, sh = sprite.get_size()
                            tw = min(MAX_SURFACE_DIM, max(1, int(round(sw * zoom))))
                            th = min(MAX_SURFACE_DIM, max(1, int(round(sh * zoom))))
                            try:
                                scaled = pygame.transform.scale(sprite, (tw, th))
                            except Exception:
                                scaled = sprite
                            _SCALED_CACHE[skey] = scaled
                        px = int(round((tx - cx*cs) * TILE_SIZE * zoom))
                        py = int(round((ty - cy*cs) * TILE_SIZE * zoom))

                        surf.blit(scaled, (px, py))
            # Store updated chunk
            self.chunks_by_zoom[zoom][(cx, cy)] = surf

    def update_cells_all_zooms(self, map_model, cells):
        """
        Rebuild affected chunks for every cached zoom level.

        This ensures that when updates are applied using a different camera/zoom than
        the one currently rendered, the visible cache still reflects the changes.
        """
        if not self.chunks_by_zoom:
            return
        cs = self.chunk_size
        matrix = map_model.matrix
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        for zoom in list(self.chunks_by_zoom.keys()):
            # Ensure base cache exists for this zoom
            if zoom not in self.chunks_by_zoom:
                self._build_chunk_surfaces(map_model, zoom)
            # Precompute overlay policy per zoom rebuild
            try:
                use_ov = bool(getattr(global_map_settings, 'use_zones_json', False))
                if use_ov:
                    from pathlib import Path as _P
                    odir = getattr(global_map_settings, 'overlays_dir', None)
                    has_ov = bool(odir and len(list(_P(odir).glob('*.overlay.json'))) > 0)
                    offsets = getattr(global_map_settings, 'zone_offsets', {})
                    user_keys = [k for k in offsets.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                    overlay_blank_world = (not has_ov) and (len(user_keys) == 0)
                else:
                    overlay_blank_world = False
            except Exception:
                overlay_blank_world = False
            # Local lazy sprite cache per zoom update
            local_sprite_cache: dict[tuple[str, str|None], pygame.Surface|None] = {}

            # Compute unique chunks to rebuild at this zoom
            dirty_chunks: set[tuple[int,int]] = set()
            for r, c in set(cells):
                dirty_chunks.add((c // cs, r // cs))
            for (cx, cy) in dirty_chunks:
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
                            if not code:
                                if overlay_blank_world:
                                    continue
                                if layer != Layer.Ground:
                                    continue
                            key = (char, code)
                            sprite = local_sprite_cache.get(key)
                            if sprite is None and key not in local_sprite_cache:
                                sprite = get_sprite_for_tile(char, code)
                                local_sprite_cache[key] = sprite
                            if not sprite:
                                continue
                            skey = (id(sprite), zoom)
                            scaled = _SCALED_CACHE.get(skey)
                            if scaled is None:
                                sw, sh = sprite.get_size()
                                tw = min(MAX_SURFACE_DIM, max(1, int(round(sw * zoom))))
                                th = min(MAX_SURFACE_DIM, max(1, int(round(sh * zoom))))
                                try:
                                    scaled = pygame.transform.scale(sprite, (tw, th))
                                except Exception:
                                    scaled = sprite
                                _SCALED_CACHE[skey] = scaled
                            px = int(round((tx - cx*cs) * TILE_SIZE * zoom))
                            py = int(round((ty - cy*cs) * TILE_SIZE * zoom))
                            surf.blit(scaled, (px, py))
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
        # Hard guard: if overlays-driven AND there are no overlay files AND no user-defined zones, render nothing
        try:
            from pathlib import Path as _P
            if getattr(global_map_settings, 'use_zones_json', False):
                odir = getattr(global_map_settings, 'overlays_dir', None)
                if odir and len(list(_P(odir).glob('*.overlay.json'))) == 0:
                    try:
                        offsets = getattr(global_map_settings, 'zone_offsets', {})
                        user_keys = [k for k in offsets.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                    except Exception:
                        user_keys = []
                    if len(user_keys) == 0:
                        if DEBUG_CHUNKED:
                            logger.debug(f"[ChunkedMapView] overlays-driven + no overlay files in {odir} -> skip render")
                        return dirty_rects
        except Exception:
            pass
        screen_w, screen_h = screen.get_size()
        # Use clamped zoom to avoid extreme surface sizes
        zoom = min(max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1), MAX_ZOOM)

        # rebuild cache para este zoom si falta
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)

        chunks = self.chunks_by_zoom[zoom]
        cs = self.chunk_size

        left   = camera.offset_x
        top    = camera.offset_y
        right  = camera.offset_x + screen_w  / zoom
        bottom = camera.offset_y + screen_h / zoom

        n_chunks_x = math.ceil(len(map_model.matrix[0]) / cs) if map_model.matrix else 0
        n_chunks_y = math.ceil(len(map_model.matrix) / cs)

        chunk_w_px = cs * TILE_SIZE
        min_cx = max(int(math.floor(left   / chunk_w_px)), 0)
        max_cx = min(int(math.ceil (right  / chunk_w_px)), n_chunks_x)
        min_cy = max(int(math.floor(top    / chunk_w_px)), 0)
        max_cy = min(int(math.ceil (bottom / chunk_w_px)), n_chunks_y)

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