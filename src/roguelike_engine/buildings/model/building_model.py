# src/roguelike_engine/buildings/model/building_model.py

import os
import types
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings

class BuildingModel:
    """
    Modelo de datos para un edificio:
    • Coordenadas relativas en su zona (rel_x, rel_y).
    • Ruta de la imagen, propiedades físicas (solid, escala original, split).
    • Cálculos de colisión (tiles, rectángulo) y propiedades de tamaño.
    """

    def __init__(
        self,
        rel_x: int,
        rel_y: int,
        image_path: str,
        solid: bool = True,
        scale: tuple[int,int] | None = None,
        *,
        split_ratio: float = 0.5,
        z_bottom: int | None = None,
        z_top: int | None = None
    ):
        # ── Datos de posición relativa y zona (se asigna externamente) ──
        self.rel_x = rel_x
        self.rel_y = rel_y
        self.zone: str | None = None

        # ── Propiedades del edificio ──
        self.solid = solid
        self.image_path = image_path
        self.split_ratio = max(0.0, min(split_ratio, 1.0))

        # ── Caches internos, inicializados en la "lógica de carga" ──
        self.image: pygame.Surface | None = None
        self.original_scale: tuple[int,int] | None = None
        self._collision_map: list[list[str]] = []
        self._collision_tiles_cache: list[pygame.Rect] | None = None
        self._collision_tile_objs: list[types.SimpleNamespace] | None = None

        # ── Z-layers por defecto (se pueden sobreescribir) ──
        from roguelike_game.systems.config_z_layer import Z_LAYERS
        self.z_bottom = z_bottom if z_bottom is not None else Z_LAYERS["building_low"]
        self.z_top    = z_top    if z_top    is not None else Z_LAYERS["building_high"]
        self.z = self.z_bottom  # compatibilidad temporal

        # ── Al final, llamamos a una rutina privada para cargar y escalar la imagen ──
        self._load_and_prepare_image(scale)

    def __repr__(self) -> str:
        name = os.path.basename(self.image_path)
        w, h = self.original_scale or (0,0)
        return (f"<BuildingModel '{name}' rel=({self.rel_x},{self.rel_y}) zone={self.zone!r} "
                f"size=({w}x{h}) split={self.split_ratio:.2f} "
                f"Zs=({self.z_bottom},{self.z_top}) solid={self.solid}>")

    # ───────────── Propiedades de posición absoluta ─────────────
    @property
    def x(self) -> int:        
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        return ox * TILE_SIZE + self.rel_x

    @x.setter
    def x(self, value: int):
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        px = int(value)
        self.rel_x = px - ox * TILE_SIZE

    @property
    def y(self) -> int:        
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        return oy * TILE_SIZE + self.rel_y

    @y.setter
    def y(self, value: int):
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        py = int(value)
        self.rel_y = py - oy * TILE_SIZE

    # ───────────── Lógica de carga y escalado inicial ─────────────
    def _load_and_prepare_image(self, scale: tuple[int,int] | None):
        """
        Carga la imagen usando pygame y la escalada inicial:
        • Si se proporciona 'scale', la aplica directamente.
        • Si la imagen es muy grande (>512×512), reduce a 1/4.
        • Guarda en self.image y self.original_scale.
        """
        from roguelike_engine.utils.loader import load_image

        surf = load_image(self.image_path)
        if scale:
            surf = pygame.transform.scale(surf, scale)
            self.original_scale = scale
        else:
            w, h = surf.get_size()
            if w > 512 or h > 512:
                new_size = (w // 4, h // 4)
                surf = pygame.transform.scale(surf, new_size)
                self.original_scale = new_size
            else:
                self.original_scale = (w, h)
        self.image = surf
        # Después de cambiar el tamaño, recalcular el “corte” en píxeles:
        self._cut_world = int(self.image.get_height() * self.split_ratio)

    # ───────────── Métodos de redimensionamiento ─────────────
    def resize(self, new_width: int, new_height: int):
        """
        Redimensiona a new_width×new_height recargando la imagen desde disco:
        • Limpia caches relacionados con el renderizado.
        • Recalcula self._cut_world para el split.
        """
        from roguelike_engine.utils.loader import load_image
        surf = load_image(self.image_path)
        surf = pygame.transform.scale(surf, (new_width, new_height))
        self.image = surf
        self.original_scale = (new_width, new_height)
        self._cut_world = int(new_height * self.split_ratio)
        # Invalidar caches de colisión y renderizado (se regenerarán cuando sea necesario)
        self._collision_tiles_cache = None
        self._collision_tile_objs = None

    def reset_to_original_size(self):
        """
        Restaura el tamaño original (self.original_scale) recargando la imagen.
        """
        if self.original_scale:
            w, h = self.original_scale
            self.resize(w, h)
        else:
            print("⚠️ No se encontró escala original para este edificio.")

    # ───────────── Colisiones (collision_map + collision_tiles) ─────────────
    @property
    def collision_rect(self) -> pygame.Rect:
        """
        Retorna el rectángulo de colisión real, que corresponde a la parte inferior
        del edificio (después de aplicar el split en self._cut_world).
        """
        full_h = self.image.get_height()
        cut_h = int(full_h * self.split_ratio)
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
            cache: list[pygame.Rect] = []
            for row_idx, row in enumerate(self._collision_map):
                for col_idx, cell in enumerate(row):
                    if cell == "#":
                        x = self.x + col_idx * TILE_SIZE
                        y = self.y + row_idx * TILE_SIZE
                        cache.append(pygame.Rect(x, y, TILE_SIZE, TILE_SIZE))
            self._collision_tiles_cache = cache
            # También creamos una lista de SimpleNamespace para quien necesite .solid y .rect
            self._collision_tile_objs = [
                types.SimpleNamespace(solid=True, rect=rect)
                for rect in cache
            ]
        return self._collision_tiles_cache

    @property
    def collision_tile_objs(self) -> list[types.SimpleNamespace]:
        """
        Acceso rápido a los objetos de colisión (con atributos .solid y .rect).
        """
        # Asegurarnos de que collision_tiles ya haya sido calculado
        _ = self.collision_tiles
        return self._collision_tile_objs or []

    # ───────────── Acceso al mapa de colisión en bruto ─────────────
    @property
    def collision_map(self) -> list[list[str]]:
        """
        El collision_map se debe cargar por fuera (p. ej. desde JSON) antes de utilizarlo.
        Aquí sólo devolvemos la referencia. No se modifica internamente en este modelo.
        """
        return self._collision_map

    @collision_map.setter
    def collision_map(self, data: list[list[str]]):
        """
        Setter que invalida el cache de collision_tiles cuando cambia el mapa.
        """
        self._collision_map = data
        self._collision_tiles_cache = None
        self._collision_tile_objs = None
