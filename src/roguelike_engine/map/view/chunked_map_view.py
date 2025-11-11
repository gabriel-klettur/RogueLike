import pygame
import math
import json
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.model.map_model import Map as MapModel
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.map.view.constants import DEBUG_CHUNKED, MAX_ZOOM
from roguelike_engine.map.view.overlay_policy import resolve_overlay_policy
from roguelike_engine.map.view.sprite_cache import SpriteScaler
from roguelike_engine.map.view.sprite_resolver import SpriteResolver
from roguelike_engine.map.view.chunk_surface_builder import build_chunk_surface
from roguelike_engine.config.map_config import global_map_settings

import logging
logger = logging.getLogger(__name__)

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
        # shared scaler cache for all chunks/zooms
        self._scaler = SpriteScaler()

    def _build_chunk_surfaces(self, map_model: MapModel, zoom: float):
        if DEBUG_CHUNKED:
            logger.debug(f" _build_chunk_surfaces called for zoom {zoom}")

        """
        Pre-dibuja cada chunk (bloque de tiles) en una surface escalada
        y la guarda en self.chunks_by_zoom[zoom].
        """
        # Clamp zoom
        zoom = min(max(float(zoom or 1.0), 0.1), MAX_ZOOM)
        matrix = map_model.matrix
        width  = len(matrix[0]) if matrix else 0
        height = len(matrix)
        cs = self.chunk_size

        n_chunks_x = math.ceil(width  / cs) if cs else 0
        n_chunks_y = math.ceil(height / cs) if cs else 0
        chunk_dict: dict[tuple[int,int], pygame.Surface] = {}

        overlay_only = resolve_overlay_policy()
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        resolver = SpriteResolver(overlay_only)

        for cy in range(n_chunks_y):
            for cx in range(n_chunks_x):
                surf = build_chunk_surface(
                    map_matrix=matrix,
                    layers_by_type=map_model.layers,
                    chunk=(cx, cy),
                    chunk_size=cs,
                    zoom=zoom,
                    ordered_layers=layers_ordered,
                    resolver=resolver,
                    scaler=self._scaler,
                )
                chunk_dict[(cx, cy)] = surf

        self.chunks_by_zoom[zoom] = chunk_dict

    def invalidate_cache(self):
        if DEBUG_CHUNKED:
            logger.debug(f" invalidate_cache called")

        """Forzar reconstrucción de todos los chunks en el próximo render."""
        self.chunks_by_zoom.clear()
        # Limpiar caché de escalados
        self._scaler.clear()

    def update_chunks(self, map_model, camera, cells):
        """
        Rebuild only the chunks containing the given tile coordinates.
        """
        # Determine zoom level (clamped)
        zoom = min(max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1), MAX_ZOOM)

        # Ensure base cache exists
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)

        cs = self.chunk_size
        matrix = map_model.matrix
        layers_ordered = sorted(map_model.layers.keys(), key=lambda l: l.value)
        overlay_only = resolve_overlay_policy()
        resolver = SpriteResolver(overlay_only)

        # Compute unique chunks to rebuild (coalesce many cells per chunk)
        dirty_chunks: set[tuple[int,int]] = set()
        for row, col in set(cells):
            dirty_chunks.add((col // cs, row // cs))

        # Rebuild each affected chunk only once
        for (cx, cy) in dirty_chunks:
            surf = build_chunk_surface(
                map_matrix=matrix,
                layers_by_type=map_model.layers,
                chunk=(cx, cy),
                chunk_size=cs,
                zoom=zoom,
                ordered_layers=layers_ordered,
                resolver=resolver,
                scaler=self._scaler,
            )
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
            overlay_only = resolve_overlay_policy()
            resolver = SpriteResolver(overlay_only)

            # Compute unique chunks to rebuild at this zoom
            dirty_chunks: set[tuple[int,int]] = set()
            for r, c in set(cells):
                dirty_chunks.add((c // cs, r // cs))
            for (cx, cy) in dirty_chunks:
                surf = build_chunk_surface(
                    map_matrix=matrix,
                    layers_by_type=map_model.layers,
                    chunk=(cx, cy),
                    chunk_size=cs,
                    zoom=zoom,
                    ordered_layers=layers_ordered,
                    resolver=resolver,
                    scaler=self._scaler,
                )
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
        # Use clamped zoom and ensure cache exists before any early returns
        zoom = min(max(float(getattr(camera, 'zoom', 1.0)) or 1.0, 0.1), MAX_ZOOM)
        if zoom not in self.chunks_by_zoom:
            self._build_chunk_surfaces(map_model, zoom)
        # Detect if the provided map has any non-ground overlay codes; if so, do not early-return
        has_non_ground_codes = False
        try:
            for lyr, grid in getattr(map_model, 'layers', {}).items():
                if lyr == Layer.Ground:
                    continue
                for row in grid:
                    for v in row:
                        if v:
                            has_non_ground_codes = True
                            raise StopIteration  # break all loops
        except StopIteration:
            pass

        # Si la vista de cámara no intersecta con los límites del mapa, no hay chunks visibles
        # y debemos devolver [] (ningún dirty rect), incluso en mundos overlay-only.
        try:
            screen_w, screen_h = screen.get_size()
            width_tiles = len(map_model.matrix[0]) if map_model.matrix else 0
            height_tiles = len(map_model.matrix)
            left = float(getattr(camera, 'offset_x', 0) or 0)
            top = float(getattr(camera, 'offset_y', 0) or 0)
            right = left + (screen_w / zoom)
            bottom = top + (screen_h / zoom)
            map_right = width_tiles * TILE_SIZE
            map_bottom = height_tiles * TILE_SIZE
            # No intersección de rectángulos en coordenadas de mundo
            if right <= 0 or bottom <= 0 or left >= map_right or top >= map_bottom:
                return []
        except Exception:
            # En caso de cualquier problema de cálculo, continuamos con la ruta normal
            pass

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
                                try:
                                    data = json.loads(txt)
                                    user_keys = [k for k in data.keys() if str(k).lower() not in ('no zone', 'no-zone')]
                                except Exception:
                                    user_keys = []
                            else:
                                user_keys = []
                    except Exception:
                        user_keys = []
                    if len(user_keys) == 0 and not has_non_ground_codes:
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
                    if stems.issubset({'no zone', 'no-zone'}) and not has_non_ground_codes:
                        if DEBUG_CHUNKED:
                            logger.debug(f"[ChunkedMapView] blank world + only sentinel overlays in {odir} -> skip render")
                        return [screen.get_rect()]
        except Exception:
            pass
        screen_w, screen_h = screen.get_size()

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