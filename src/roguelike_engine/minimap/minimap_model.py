import pygame
from typing import Optional, Dict, Tuple, List

from roguelike_engine.config.config_minimap import (
    MINIMAP_WIDTH,
    MINIMAP_HEIGHT,
    MINIMAP_ZOOM,
    MINIMAP_PADDING,
    MINIMAP_BG_ALPHA,
)


class MinimapModel:
    """
    Modelo de datos del Minimap. Mantiene tamaño, zoom, pads, surfaces por capa,
    estados de actualización (rate-limits) y flags de UI.
    """

    def __init__(self) -> None:
        # Dimensiones y layout
        self.width: int = MINIMAP_WIDTH
        self.height: int = MINIMAP_HEIGHT
        self.zoom: int = MINIMAP_ZOOM
        self.pad_x, self.pad_y = MINIMAP_PADDING

        # Superficie final y capas internas
        self.surface: pygame.Surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.surface.set_alpha(MINIMAP_BG_ALPHA)
        self.bg_tiles_surface: pygame.Surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.buildings_surface: pygame.Surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.entities_surface: pygame.Surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)
        self.zones_surface: pygame.Surface = pygame.Surface((self.width, self.height), pygame.SRCALPHA)

        # Tiempos de última actualización (rate-limits)
        self.last_tiles_ms: int = 0
        self.last_buildings_ms: int = 0
        self.last_entities_ms: int = 0
        self.last_zones_ms: int = 0

        # Cache de últimos parámetros relevantes
        self.last_player_tile: Optional[Tuple[int, int]] = None
        self.last_world_id: Optional[str] = None
        self.visible_half_tiles: Tuple[int, int] = (
            (self.width // self.zoom) // 2,
            (self.height // self.zoom) // 2,
        )

        # Datos visibles
        self.visible_tiles: List[object] = []  # lista de Tile, pero tipado laxo para evitar dependencias cruzadas

        # Flags de visibilidad de capas
        self.show_tiles: bool = True
        self.show_buildings: bool = True
        self.show_entities: bool = True
        self.show_zones: bool = True

        # Estado de UI (botones y hover)
        self.btn_rects: Dict[str, pygame.Rect] = {}
        self.btn_hover: Optional[str] = None
        self.font: Optional[pygame.font.Font] = None

        # Último rect de render para hit-test
        self.last_rect: Optional[pygame.Rect] = None
