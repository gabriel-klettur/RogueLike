# Path: src/roguelike_engine/minimap/minimap.py
import pygame
from typing import Tuple, Iterable
from roguelike_engine.config_tiles import TILE_SIZE, TILE_COLORS
from roguelike_engine.tiles.model import Tile

class Minimap:
    def __init__(
        self,
        width: int = 200,
        height: int = 150,
        zoom: int = 1,
        padding: Tuple[int, int] = (20, 20),
    ):
        self.width = width
        self.height = height
        self.zoom = zoom
        self.pad_x, self.pad_y = padding

        self.surface = pygame.Surface((width, height), pygame.SRCALPHA)
        self.surface.set_alpha(180)

    def update(
        self,
        player_pos: Tuple[float, float],
        tiles: Iterable[Tile],
    ):
        px = int(player_pos[0]) // TILE_SIZE
        py = int(player_pos[1]) // TILE_SIZE
        self.center_tile = (px, py)

        # calcular mitad en tiles
        half_x = (self.width // self.zoom) // 2
        half_y = (self.height // self.zoom) // 2

        self.visible_tiles = [
            t for t in tiles
            if abs((t.x // TILE_SIZE) - px) <= half_x
            and abs((t.y // TILE_SIZE) - py) <= half_y
        ]

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        self.surface.fill((10, 10, 10))
        cx, cy = self.center_tile

        for t in self.visible_tiles:
            tx = (t.x // TILE_SIZE) - cx
            ty = (t.y // TILE_SIZE) - cy
            x = self.width // 2 + tx * self.zoom
            y = self.height // 2 + ty * self.zoom
            color = TILE_COLORS.get(t.tile_type, (255, 0, 255))
            pygame.draw.rect(self.surface, color, (x, y, self.zoom, self.zoom))

        # jugador
        pygame.draw.rect(
            self.surface,
            (0, 255, 0),
            (self.width // 2, self.height // 2, self.zoom, self.zoom)
        )

        # blit + retorno del área sucia
        dest = (screen.get_width() - self.width - self.pad_x, self.pad_y)
        screen.blit(self.surface, dest)
        return pygame.Rect(dest, (self.width, self.height))
