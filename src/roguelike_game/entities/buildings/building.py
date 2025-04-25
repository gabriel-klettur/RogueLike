# src.roguelike_project/entities/buildings/building.py
import os
import pygame
from src.roguelike_engine.utils.loader import load_image
import src.roguelike_project.config as config
from src.roguelike_game.systems.z_layer.config import Z_LAYERS

class Building:
    """
    Un edificio ahora se divide en dos mitades (bottom / top) separadas
    por `split_ratio` (0‑1). Cada mitad puede pertenecer a capas Z distintas.
    """
    def __init__(
        self,
        x,
        y,
        image_path,
        solid=True,
        scale=None,
        *,
        split_ratio: float = 0.5,
        z_bottom: int | None = None,
        z_top: int | None = None
    ):
        self.x = x
        self.y = y
        self.solid = solid
        self.image_path = image_path
        self.scaled_cache: dict[float, pygame.Surface] = {}

        self.image = load_image(image_path)
        if scale:
            self.image = pygame.transform.scale(self.image, scale)
            self.original_scale = scale
        else:
            self.original_scale = self.image.get_size()

        # --- Nueva info de división ---
        self.split_ratio = max(0.0, min(split_ratio, 1.0))
        self.z_bottom = z_bottom if z_bottom is not None else Z_LAYERS["building_low"]
        self.z_top    = z_top    if z_top    is not None else Z_LAYERS["building_high"]

        # compatibilidad (algunos sistemas aún miran `z`)
        self.z = self.z_bottom

        self.rect = pygame.Rect(self.x, self.y, *self.image.get_size())

    # ------------------------------------------------------------------ #
    # Representación legible                                              #
    # ------------------------------------------------------------------ #
    def __repr__(self) -> str:
        name = os.path.basename(self.image_path)
        w, h = self.image.get_size()
        return (f"<Building '{name}' pos=({self.x:.0f},{self.y:.0f}) "
                f"size=({w}x{h}) split={self.split_ratio:.2f} "
                f"Zs=({self.z_bottom},{self.z_top}) solid={self.solid}>")

    # ------------------------------------------------------------------ #
    # Utilidades internas                                                #
    # ------------------------------------------------------------------ #
    def _get_scaled_image(self, camera):
        zoom = round(camera.zoom, 2)
        if zoom not in self.scaled_cache:
            self.scaled_cache[zoom] = pygame.transform.scale(
                self.image, camera.scale(self.image.get_size())
            )
        return self.scaled_cache[zoom]

    # ------------------------------------------------------------------ #
    # Render de cada mitad                                               #
    # ------------------------------------------------------------------ #
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
            # Dibujar hitbox visual solo en la parte baja
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 255), scaled_rect, 1)

    # ------------------------------------------------------------------ #
    # Interfaz pública para el renderer                                  #
    # ------------------------------------------------------------------ #
    class _BuildingPart:
        """Wrapper ligero que representa una de las mitades."""
        __slots__ = ("_parent", "_top")
        def __init__(self, parent: "Building", top: bool):
            self._parent = parent
            self._top = top
            # Se asignará la Z en el loop de render

        # Propiedades que el renderer usa -------------------------------
        @property
        def x(self): return self._parent.x
        @property
        def y(self): return self._parent.y
        @property
        def z(self):  # para lectura rápida
            return self._parent.z_top if self._top else self._parent.z_bottom
        @property
        def sprite_size(self):
            return self._parent.image.get_size()

        # Método que llama el renderer
        def render(self, screen, camera):
            self._parent._render_part(screen, camera, top=self._top)

    def get_parts(self):
        """Devuelve [parte_inferior, parte_superior] (orden importante)."""
        return [
            Building._BuildingPart(self, top=False),
            Building._BuildingPart(self, top=True)
        ]

    # ------------------------------------------------------------------ #
    # Métodos existentes (resize / reset) permanecen sin cambios         #
    # ------------------------------------------------------------------ #
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
