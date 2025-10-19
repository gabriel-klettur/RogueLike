from __future__ import annotations

import types
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.buildings.services.collisions import (
    image_to_grid_size,
    resample_collision_map,
)
from roguelike_engine.buildings.model_utils.image_ops import (
    build_full_mask as _mu_build_full_mask,
)
from roguelike_engine.buildings.model_utils.collision_ops import (
    build_collision_tiles as _mu_build_collision_tiles,
    build_collision_tile_objs as _mu_build_collision_tile_objs,
)
from roguelike_engine.buildings.services.types import CollisionMap


class BuildingCollisionMixin:
    # ----------------------------- Collision mask (alpha-based) -----------------------------
    def get_full_mask(self) -> pygame.Mask | None:
        """Return (and cache) a mask built from the full image alpha. None if no image."""
        try:
            if self.image is None:
                return None
            if self._mask_full is None:
                self._mask_full = _mu_build_full_mask(self.image)
            return self._mask_full
        except Exception:
            return None

    # ───────────── Colisiones (collision_map + collision_tiles) ─────────────
    @property
    def collision_rect(self) -> pygame.Rect:
        """
        Retorna el rectángulo de colisión real, que corresponde a la parte inferior
        del edificio (después de aplicar el split en self._cut_world).
        """
        full_h = self.image.get_height()
        cut_h = self._cut_world
        return pygame.Rect(
            self.x,
            self.y + cut_h,
            self.image.get_width(),
            full_h - cut_h
        )

    @property
    def collision_tiles(self) -> list[pygame.Rect]:
        """
        Construye, si no existe, la lista de rectángulos de colisión por cada celda '#'
        de self._collision_map. Crea también objetos envoltorio con flag 'solid'.
        """
        if self._collision_tiles_cache is None:
            cache: list[pygame.Rect] = _mu_build_collision_tiles(
                self._collision_map,
                base_x=self.x,
                base_y=self.y,
                tile_size=TILE_SIZE,
            )
            self._collision_tiles_cache = cache
            # También creamos una lista de SimpleNamespace para quien necesite .solid y .rect
            self._collision_tile_objs = _mu_build_collision_tile_objs(cache)
        return self._collision_tiles_cache

    @property
    def collision_tile_objs(self) -> list[types.SimpleNamespace]:
        """
        Acceso rápido a los objetos de colisión (con atributos .solid y .rect).
        """
        # Asegurarnos de que collision_tiles ya haya sido calculado
        _ = self.collision_tiles
        return self._collision_tile_objs or []

    # ───────────── Utilidades de grid para collision_map ─────────────
    def _image_to_grid_size(self) -> tuple[int, int]:
        """
        Devuelve (rows, cols) de la grilla de colisión a partir del tamaño de la imagen.
        Garantiza al menos 1×1 celdas para permitir edición incluso en tamaños pequeños.
        """
        return image_to_grid_size(self.image, TILE_SIZE)

    def _resample_collision_map(self, new_rows: int, new_cols: int):
        """
        Redimensiona self._collision_map a (new_rows×new_cols) usando remuestreo
        nearest-neighbor para preservar lo existente al escalar.
        Si el mapa actual es vacío, inicializa con '.' (caminable).
        """
        self._collision_map = resample_collision_map(self._collision_map, new_rows, new_cols)

    # ───────────── Acceso al mapa de colisión en bruto ─────────────
    @property
    def collision_map(self) -> CollisionMap:
        """
        El collision_map se debe cargar por fuera (p. ej. desde JSON) antes de utilizarlo.
        Aquí sólo devolvemos la referencia. No se modifica internamente en este modelo.
        """
        return self._collision_map

    @collision_map.setter
    def collision_map(self, data: CollisionMap):
        """
        Setter que invalida el cache de collision_tiles cuando cambia el mapa.
        """
        self._collision_map = data
        self._collision_tiles_cache = None
        self._collision_tile_objs = None

    # ───────────── Invalidation helpers ─────────────
    def invalidate_collision_caches(self) -> None:
        """Invalida caches derivados de collision_map (rects y objetos)."""
        self._collision_tiles_cache = None
        self._collision_tile_objs = None
