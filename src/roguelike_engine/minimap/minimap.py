# Path: src/roguelike_engine/minimap/minimap.py
import pygame
from typing import Tuple, Iterable
from roguelike_engine.config_tiles import TILE_SIZE, TILE_COLORS
from roguelike_engine.tiles.model import Tile

class Minimap:
    def __init__(self, width=200, height=150, zoom=1, padding: Tuple[int,int]=(20,20)):
        self.width = width
        self.height = height
        self.zoom = zoom
        self.pad_x, self.pad_y = padding

        # Superficie final donde dibujamos:
        self.surface = pygame.Surface((width, height), pygame.SRCALPHA)
        self.surface.set_alpha(180)

        # Superficie cacheada de background (sólo tiles):
        self.bg_surface = pygame.Surface((width, height), pygame.SRCALPHA)
        self._last_center = None  # para detectar cambios
        self.visible_tiles = []

    def update(self, player_pos: Tuple[float,float], tiles: Iterable[Tile]):
        px = int(player_pos[0]) // TILE_SIZE
        py = int(player_pos[1]) // TILE_SIZE
        center = (px, py)

        # Si cruzó a otro tile central, rehacemos background
        if center != self._last_center:
            self._last_center = center
            half_x = (self.width // self.zoom) // 2
            half_y = (self.height // self.zoom) // 2

            # filtrar once
            self.visible_tiles = [
                t for t in tiles
                if abs((t.x//TILE_SIZE)-px) <= half_x
                and abs((t.y//TILE_SIZE)-py) <= half_y
            ]

            # regenerar bg_surface
            self.bg_surface.fill((10,10,10))
            for t in self.visible_tiles:
                tx = (t.x // TILE_SIZE) - px
                ty = (t.y // TILE_SIZE) - py
                x = self.width//2 + tx*self.zoom
                y = self.height//2 + ty*self.zoom
                color = TILE_COLORS.get(t.tile_type, (255,0,255))
                # dibujamos directamente en bg_surface
                pygame.draw.rect(self.bg_surface, color, (x,y,self.zoom,self.zoom))

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        # sólo blitear bg + jugador
        self.surface.blit(self.bg_surface, (0,0))

        # jugador
        pygame.draw.rect(
            self.surface,
            (0,255,0),
            (self.width//2, self.height//2, self.zoom, self.zoom)
        )

        dest = (screen.get_width() - self.width - self.pad_x, self.pad_y)
        screen.blit(self.surface, dest)
        return pygame.Rect(dest, (self.width, self.height))
