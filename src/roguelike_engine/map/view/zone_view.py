# src/roguelike_engine/map/view/zone_view.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.tile.model.tile import Tile
from roguelike_engine.config.config import DEBUG


class ZoneView:
    """
    Renderiza una sola zona: todos sus tiles y opcionalmente su contorno.
    """
    def __init__(self, border_width: int = 3):
        self.border_width = border_width
        # Colores por zona, personalizables
        self.colors = {
            zone: (200, 200, 200)
            for zone in global_map_settings.zone_offsets
        }
        # Ejemplos de colores especiales
        if 'lobby' in self.colors:
            self.colors['lobby'] = (255, 255, 0)
        if 'dungeon' in self.colors:
            self.colors['dungeon'] = (0, 255, 0)

    def render_zone(
        self,
        screen: pygame.Surface,
        camera,
        zone_name: str,
        tiles: list[Tile]
    ):
        # 1) Dibujar todos los sprites de la zona
        for tile in tiles:
            # Obtener sprite escalado o original
            z = round(camera.zoom, 2)
            sprite = tile.scaled_cache.get(z)
            if sprite is None:
                sprite = tile.sprite
            screen.blit(sprite, camera.apply((tile.x, tile.y)))

        # 2) Dibujar contorno de la zona (bounding box)
        if not tiles or not DEBUG:
            return

        xs = [t.x for t in tiles]
        ys = [t.y for t in tiles]
        min_x, max_x = min(xs), max(xs) + TILE_SIZE
        min_y, max_y = min(ys), max(ys) + TILE_SIZE

        top_left     = camera.apply((min_x, min_y))
        bottom_right = camera.apply((max_x, max_y))
        w = bottom_right[0] - top_left[0]
        h = bottom_right[1] - top_left[1]

        rect = pygame.Rect(top_left, (w, h))
        color = self.colors.get(zone_name, (200, 200, 200))
        pygame.draw.rect(screen, color, rect, self.border_width)