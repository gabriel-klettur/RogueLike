import pygame
import math
import json
from roguelike_engine.config.config_tiles import TILE_SIZE, OVERLAY_CODE_MAP
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
            cur_world = str(getattr(global_map_settings, 'current_world', 'base'))
            # Prefer MapSettings.is_blank_world() for a robust decision
            try:
                overlay_no_fallback = bool(getattr(global_map_settings, 'is_blank_world', lambda: False)())
            except Exception:
                overlay_no_fallback = False
            # Fallback to zones.json direct check if helper not available
            if overlay_no_fallback is False:
                zones_empty = False
                z = getattr(global_map_settings, 'ZONES_INDEX', None)
                try:
                    if z and z.exists():
                        txt = z.read_text(encoding='utf-8').strip()
                        if txt:
                            try:
                                data = json.loads(txt)
                                zones_empty = isinstance(data, dict) and len(data) == 0
                            except Exception:
                                zones_empty = False
                        else:
                            zones_empty = True
                    else:
                        zones_empty = True
                except Exception:
                    zones_empty = True
                overlay_no_fallback = zones_empty
            # If overlays directory has no files or only sentinel overlays, force overlay-only policy
            try:
                from pathlib import Path as _P
                odir = getattr(global_map_settings, 'overlays_dir', None)
                files = list(_P(odir).glob('*.overlay.json')) if odir else []
                if files:
                    # Normalize names: 'no zone.overlay.json' -> 'no zone'
                    stems = {
                        (s[:-8] if s.endswith('.overlay') else s)
                        for s in (f.stem.lower().replace('_', ' ') for f in files)
                    }
                    if stems.issubset({'no zone', 'no-zone'}):
                        overlay_no_fallback = True
                else:
                    # No overlays at all in this world
                    overlay_no_fallback = True
            except Exception:
                pass
        except Exception:
            overlay_no_fallback = False
        try:
            last = getattr(self, "_last_policy_log", None)
            if last is not overlay_no_fallback:
                logger.info(f"[ChunkedMapView] overlay_no_fallback={overlay_no_fallback} world={getattr(global_map_settings,'current_world','?')}")
                self._last_policy_log = overlay_no_fallback
                # Diagnostic: when enabled, report counts of Ground codes
                if overlay_no_fallback:
                    try:
                        g = map_model.layers.get(Layer.Ground)
                        if g:
                            nonempty = 0
                            empty = 0
                            valid = 0
                            invalid = 0
                            for row in g:
                                for v in row:
                                    if not v:
                                        empty += 1
                                    else:
                                        nonempty += 1
                                        if v in OVERLAY_CODE_MAP:
                                            valid += 1
                                        else:
                                            invalid += 1
                            logger.info(f"[ChunkedMapView] ground_counts empty={empty} nonempty={nonempty} valid={valid} invalid={invalid}")
                    except Exception:
                        pass
        except Exception:
            pass

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
                        # In overlay-only mode, do not fallback to base sprite if overlay code is unknown
                        if overlay_no_fallback and code and code not in OVERLAY_CODE_MAP:
                            sprite_map[key] = None
                        else:
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
                            # Draw policy: in overlays-driven mode for non-base or blank worlds, draw only explicit overlay codes (no Ground fallback)
                            if not code:
                                if overlay_no_fallback:
                                    continue
                                if layer != Layer.Ground:
                                    continue
                            # If code is invalid and we're in overlay-only policy, skip (avoid base fallback)
                            if overlay_no_fallback and code and code not in OVERLAY_CODE_MAP:
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
            cur_world = str(getattr(global_map_settings, 'current_world', 'base'))
            try:
                overlay_no_fallback = bool(getattr(global_map_settings, 'is_blank_world', lambda: False)())
            except Exception:
                overlay_no_fallback = False
            if overlay_no_fallback is False:
                zones_empty = False
                z = getattr(global_map_settings, 'ZONES_INDEX', None)
                try:
                    if z and z.exists():
                        txt = z.read_text(encoding='utf-8').strip()
                        if txt:
                            try:
                                data = json.loads(txt)
                                zones_empty = isinstance(data, dict) and len(data) == 0
                            except Exception:
                                zones_empty = False
                        else:
                            zones_empty = True
                    else:
                        zones_empty = True
                except Exception:
                    zones_empty = True
                overlay_no_fallback = zones_empty
            # Overlays directory sentinel-only or empty -> force overlay-only
            try:
                from pathlib import Path as _P
                odir = getattr(global_map_settings, 'overlays_dir', None)
                files = list(_P(odir).glob('*.overlay.json')) if odir else []
                if files:
                    stems = {
                        (s[:-8] if s.endswith('.overlay') else s)
                        for s in (f.stem.lower().replace('_', ' ') for f in files)
                    }
                    if stems.issubset({'no zone', 'no-zone'}):
                        overlay_no_fallback = True
                else:
                    overlay_no_fallback = True
            except Exception:
                pass
        except Exception:
            overlay_no_fallback = False
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
            # Match _build_chunk_surfaces: start from opaque black background so
            # empty/overlay-only chunks remain solid black rather than transparent
            try:
                surf.fill((0, 0, 0, 255))
            except Exception:
                pass
            for ty in range(cy*cs, cy*cs + tile_h):
                for tx in range(cx*cs, cx*cs + tile_w):
                    char = matrix[ty][tx]
                    for layer in layers_ordered:
                        code = map_model.layers[layer][ty][tx]
                        if not code:
                            if overlay_no_fallback:
                                continue
                            if layer != Layer.Ground:
                                continue
                        # If code is invalid under overlay-only policy, skip drawing
                        if overlay_no_fallback and code and code not in OVERLAY_CODE_MAP:
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
                cur_world = str(getattr(global_map_settings, 'current_world', 'base'))
                try:
                    overlay_no_fallback = bool(getattr(global_map_settings, 'is_blank_world', lambda: False)())
                except Exception:
                    overlay_no_fallback = False
                if overlay_no_fallback is False:
                    zones_empty = False
                    z = getattr(global_map_settings, 'ZONES_INDEX', None)
                    try:
                        if z and z.exists():
                            txt = z.read_text(encoding='utf-8').strip()
                            if txt:
                                try:
                                    data = json.loads(txt)
                                    zones_empty = isinstance(data, dict) and len(data) == 0
                                except Exception:
                                    zones_empty = False
                            else:
                                zones_empty = True
                        else:
                            zones_empty = True
                    except Exception:
                        zones_empty = True
                    overlay_no_fallback = zones_empty
                # Overlays directory sentinel-only or empty -> force overlay-only
                try:
                    from pathlib import Path as _P
                    odir = getattr(global_map_settings, 'overlays_dir', None)
                    files = list(_P(odir).glob('*.overlay.json')) if odir else []
                    if files:
                        stems = {
                            (s[:-8] if s.endswith('.overlay') else s)
                            for s in (f.stem.lower().replace('_', ' ') for f in files)
                        }
                        if stems.issubset({'no zone', 'no-zone'}):
                            overlay_no_fallback = True
                    else:
                        overlay_no_fallback = True
                except Exception:
                    pass
            except Exception:
                overlay_no_fallback = False
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
                # Keep consistency with initial chunk build: opaque black background
                try:
                    surf.fill((0, 0, 0, 255))
                except Exception:
                    pass
                for ty in range(cy*cs, cy*cs + tile_h):
                    for tx in range(cx*cs, cx*cs + tile_w):
                        char = matrix[ty][tx]
                        for layer in layers_ordered:
                            code = map_model.layers[layer][ty][tx]
                            if not code:
                                if overlay_no_fallback:
                                    continue
                                if layer != Layer.Ground:
                                    continue
                            if overlay_no_fallback and code and code not in OVERLAY_CODE_MAP:
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
        # Hard guard 1: if overlays-driven AND there are no overlay files AND no user-defined zones, render nothing
        try:
            from pathlib import Path as _P
            if getattr(global_map_settings, 'use_zones_json', False):
                odir = getattr(global_map_settings, 'overlays_dir', None)
                if odir and len(list(_P(odir).glob('*.overlay.json'))) == 0:
                    user_keys = []
                    try:
                        z = getattr(global_map_settings, 'ZONES_INDEX', None)
                        if z and z.exists():
                            txt = z.read_text(encoding='utf-8').strip()
                            if txt:
                                data = json.loads(txt)
                                if isinstance(data, dict):
                                    user_keys = [k for k in data.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                    except Exception:
                        user_keys = []
                    if len(user_keys) == 0:
                        if DEBUG_CHUNKED:
                            logger.debug(f"[ChunkedMapView] overlays-driven + no overlay files in {odir} -> skip render")
                        return [screen.get_rect()]
        except Exception:
            pass
        # Hard guard 2: if world is blank AND overlays directory contains only sentinel overlays, skip drawing
        try:
            if getattr(global_map_settings, 'is_blank_world', None) and global_map_settings.is_blank_world():
                from pathlib import Path as _P
                odir = getattr(global_map_settings, 'overlays_dir', None)
                files = list(_P(odir).glob('*.overlay.json')) if odir else []
                if files:
                    stems = {
                        (s[:-8] if s.endswith('.overlay') else s)
                        for s in (f.stem.lower().replace('_', ' ') for f in files)
                    }
                    if stems.issubset({'no zone', 'no-zone'}):
                        if DEBUG_CHUNKED:
                            logger.debug(f"[ChunkedMapView] blank world + only sentinel overlays in {odir} -> skip render")
                        return [screen.get_rect()]
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