from pygame import Surface
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from .colors import Palette


class CollidersView:
    """Renders collider overlays for solid tiles."""

    def __init__(self, palette: Palette) -> None:
        self.palette = palette

    def render(self, screen: Surface, camera, map_manager) -> None:
        for tile in map_manager.solid_tiles:
            tl = camera.apply((tile.x, tile.y))
            size = camera.scale((TILE_SIZE, TILE_SIZE))
            overlay = pygame.Surface(size, pygame.SRCALPHA)
            overlay.fill(self.palette.collider_fill)
            screen.blit(overlay, tl)
            pygame.draw.rect(screen, self.palette.collider_border, (*tl, *size), 1)
