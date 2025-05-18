# src/roguelike_game/entities/buildings/building.py

import os
import pygame
from roguelike_engine.utils.loader import load_image
import roguelike_engine.config.config as config
from roguelike_game.systems.config_z_layer import Z_LAYERS

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

        # Carga y escala de la imagen
        self.image = load_image(image_path)
        if scale:
            self.image = pygame.transform.scale(self.image, scale)
            self.original_scale = scale
        else:
            self.original_scale = self.image.get_size()

        # División en dos mitades según split_ratio
        self.split_ratio = max(0.0, min(split_ratio, 1.0))
        self.z_bottom = z_bottom if z_bottom is not None else Z_LAYERS["building_low"]
        self.z_top    = z_top    if z_top    is not None else Z_LAYERS["building_high"]

        # Compatibilidad: algunos sistemas aún consultan `z`
        self.z = self.z_bottom

        # Rectángulo de colisión/renderizado (usa propiedades x,y)
        self.rect = pygame.Rect(self.x, self.y, *self.image.get_size())

    def __repr__(self) -> str:
        name = os.path.basename(self.image_path)
        w, h = self.image.get_size()
        return (f"<Building '{name}' rel=({self.rel_x},{self.rel_y}) zone={self.zone!r} "
                f"size=({w}x{h}) split={self.split_ratio:.2f} "
                f"Zs=({self.z_bottom},{self.z_top}) solid={self.solid}>")

    @property
    def x(self):
        from roguelike_engine.config.config_tiles import TILE_SIZE
        from roguelike_engine.config.map_config import ZONE_OFFSETS
        ox, oy = ZONE_OFFSETS.get(self.zone, (0, 0))
        return ox * TILE_SIZE + self.rel_x

    @property
    def y(self):
        from roguelike_engine.config.config_tiles import TILE_SIZE
        from roguelike_engine.config.map_config import ZONE_OFFSETS
        ox, oy = ZONE_OFFSETS.get(self.zone, (0, 0))
        return oy * TILE_SIZE + self.rel_y

    def _get_scaled_image(self, camera):
        zoom = round(camera.zoom, 2)
        if zoom not in self.scaled_cache:
            self.scaled_cache[zoom] = pygame.transform.scale(
                self.image, camera.scale(self.image.get_size())
            )
        return self.scaled_cache[zoom]

    def _render_part(self, screen, camera, *, top: bool):
        full_scaled = self._get_scaled_image(camera)
        full_w, full_h = full_scaled.get_size()

        cut_scaled = int(full_h * self.split_ratio)
        cut_world  = int(self.image.get_height() * self.split_ratio)

        if top:
            sub_rect = pygame.Rect(0, 0, full_w, cut_scaled)
            offset_y_world = 0
        else:
            sub_rect = pygame.Rect(0, cut_scaled, full_w, full_h - cut_scaled)
            offset_y_world = cut_world

        part_surface = full_scaled.subsurface(sub_rect)
        screen.blit(
            part_surface,
            camera.apply((self.x, self.y + offset_y_world))
        )

        if self.solid and config.DEBUG and not top:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 255), scaled_rect, 1)

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
        return [
            Building._BuildingPart(self, top=False),
            Building._BuildingPart(self, top=True)
        ]

    def resize(self, new_width, new_height):
        self.image = pygame.transform.scale(load_image(self.image_path), (new_width, new_height))
        self.rect = pygame.Rect(self.x, self.y, new_width, new_height)
        self.scaled_cache.clear()

    def reset_to_original_size(self):
        if self.original_scale:
            self.resize(*self.original_scale)
            print(f"↩️ Tamaño reseteado a original: {self.original_scale}")
        else:
            print("⚠️ No se encontró escala original para este edificio.")
