# src/roguelike_game/entities/buildings/building.py

import os
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_game.systems.config_z_layer import Z_LAYERS
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.utils.debug import draw_debug_rect

class Building:
    """
    Un edificio ahora se almacena con coordenadas relativas (rel_x, rel_y) dentro de su zona,
    y calcula sus posiciones absolutas (x, y) al vuelo.
    """

    def __init__(
        self,
        rel_x: int,
        rel_y: int,
        image_path,
        solid=True,
        scale=None,
        *,
        split_ratio: float = 0.5,
        z_bottom: int | None = None,
        z_top: int | None = None
    ):
        # Coordenadas relativas dentro de la zona
        self.rel_x = rel_x
        self.rel_y = rel_y
        # Zona (se asigna con assign_zone_and_relatives o al cargar desde JSON)
        self.zone = None

        self.solid = solid
        self.image_path = image_path
        self.scaled_cache: dict[float, pygame.Surface] = {}
        self._render_part_cache: dict[float, tuple[pygame.Surface, pygame.Surface]] = {}

        # Carga y escala de la imagen
        self.image = load_image(image_path)
        if scale:
            self.image = pygame.transform.scale(self.image, scale)
            self.original_scale = scale
        else:
            self.original_scale = self.image.get_size()

        # División en dos mitades según split_ratio
        self.split_ratio = max(0.0, min(split_ratio, 1.0))
        self._cut_world = int(self.image.get_height() * self.split_ratio)
        self.z_bottom = z_bottom if z_bottom is not None else Z_LAYERS["building_low"]
        self.z_top    = z_top    if z_top    is not None else Z_LAYERS["building_high"]

        # Compatibilidad: algunos sistemas aún consultan `z`
        self.z = self.z_bottom

        # Rectángulo de colisión/renderizado (usa propiedades x,y)
        self.rect = pygame.Rect(self.x, self.y, *self.image.get_size())
        # Collision map por tile (# = sólido, . = transitable)
        self.collision_map: list[list[str]] = []
        # Instancias persistentes de partes para render
        self._bottom_part = Building._BuildingPart(self, top=False)
        self._top_part    = Building._BuildingPart(self, top=True)

    def __repr__(self) -> str:
        name = os.path.basename(self.image_path)
        w, h = self.image.get_size()
        return (f"<Building '{name}' rel=({self.rel_x},{self.rel_y}) zone={self.zone!r} "
                f"size=({w}x{h}) split={self.split_ratio:.2f} "
                f"Zs=({self.z_bottom},{self.z_top}) solid={self.solid}>")

    @property
    def x(self):        
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        return ox * TILE_SIZE + self.rel_x
    @x.setter
    def x(self, value):
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        px = int(value)
        self.rel_x = px - ox * TILE_SIZE
        if hasattr(self, 'rect'):
            self.rect.x = px

    @property
    def y(self):        
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        return oy * TILE_SIZE + self.rel_y
    @y.setter
    def y(self, value):
        ox, oy = global_map_settings.zone_offsets.get(self.zone, (0, 0))
        py = int(value)
        self.rel_y = py - oy * TILE_SIZE
        if hasattr(self, 'rect'):
            self.rect.y = py

    def _get_scaled_image(self, camera):
        zoom = round(camera.zoom, 2)
        if zoom not in self.scaled_cache:
            scaled = pygame.transform.scale(
                self.image, camera.scale(self.image.get_size())
            )
            self.scaled_cache[zoom] = scaled
            # Cache top/bottom surfaces for render part
            w, h = scaled.get_size()
            cut_scaled = int(h * self.split_ratio)
            top_surf = scaled.subsurface(pygame.Rect(0, 0, w, cut_scaled)).copy()
            bottom_surf = scaled.subsurface(pygame.Rect(0, cut_scaled, w, h - cut_scaled)).copy()
            self._render_part_cache[zoom] = (top_surf, bottom_surf)
        return self.scaled_cache[zoom]

    def _render_part(self, screen, camera, *, top: bool):
        zoom = round(camera.zoom, 2)
        if zoom not in self._render_part_cache:
            # ensure part cache is built
            self._get_scaled_image(camera)
        top_surf, bottom_surf = self._render_part_cache[zoom]
        surf = top_surf if top else bottom_surf
        offset = 0 if top else self._cut_world
        screen.blit(surf, camera.apply((self.x, self.y + offset)))
        if self.solid and not top:
            draw_debug_rect(screen, camera, self.rect, color=(255,255,255), width=1)

    class _BuildingPart:
        """Wrapper ligero que representa una de las mitades."""
        __slots__ = ("_parent", "_top")

        def __init__(self, parent: "Building", top: bool):
            self._parent = parent
            self._top = top

        @property
        def x(self): return self._parent.x
        @property
        def y(self): return self._parent.y
        @property
        def z(self):
            return self._parent.z_top if self._top else self._parent.z_bottom
        @property
        def sprite_size(self):
            return self._parent.image.get_size()
        def render(self, screen, camera):
            self._parent._render_part(screen, camera, top=self._top)

    def get_parts(self):
        # Retorna instancias cacheadas de las dos mitades
        return [self._bottom_part, self._top_part]

    def resize(self, new_width, new_height):
        self.image = pygame.transform.scale(load_image(self.image_path), (new_width, new_height))
        self.rect = pygame.Rect(self.x, self.y, new_width, new_height)
        self.scaled_cache.clear()
        self._render_part_cache.clear()

    def reset_to_original_size(self):
        if self.original_scale:
            self.resize(*self.original_scale)
            print(f"↩️ Tamaño reseteado a original: {self.original_scale}")
        else:
            print("⚠️ No se encontró escala original para este edificio.")

    @property
    def collision_rect(self) -> pygame.Rect:
        """
        Rectángulo de colisión: parte inferior del edificio según split_ratio.
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
        Retorna una lista de pygame.Rect para cada celda '#' de collision_map (cacheada).
        """
        if not hasattr(self, '_collision_tiles_cache'):
            cache = []
            for row_idx, row in enumerate(self.collision_map):
                for col_idx, cell in enumerate(row):
                    if cell == '#':
                        x = self.x + col_idx * TILE_SIZE
                        y = self.y + row_idx * TILE_SIZE
                        cache.append(pygame.Rect(x, y, TILE_SIZE, TILE_SIZE))
            self._collision_tiles_cache = cache
        return self._collision_tiles_cache
