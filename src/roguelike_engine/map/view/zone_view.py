# src/roguelike_engine/map/view/zone_view.py

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings

class ZoneView:
    """
    Dibuja un borde alrededor de cada zona definida en global_map_settings.zone_offsets.
    """
    def __init__(self, border_width: int = 3):
        self.border_width = border_width
        # colores por zona (puedes personalizar)
        self.colors = {
            zone: (255, 255, 255) for zone in global_map_settings.zone_offsets
        }
        # asignar colores distintos si quieres
        self.colors.update({
            'lobby':   (255, 255, 0),
            'dungeon': (0, 255, 0),
        })

    def render(self, screen: pygame.Surface, camera, tiles_by_zone: dict[str, list]):
        for zone, tiles in tiles_by_zone.items():
            # si no hay tiles, saltar
            if not tiles:
                continue
            # calcular bounds de la zona
            xs = [t.x for t in tiles]
            ys = [t.y for t in tiles]
            min_x, max_x = min(xs), max(xs) + TILE_SIZE
            min_y, max_y = min(ys), max(ys) + TILE_SIZE

            # convertir al espacio de pantalla con la cámara
            top_left     = camera.apply((min_x, min_y))
            bottom_right = camera.apply((max_x, max_y))
            w = bottom_right[0] - top_left[0]
            h = bottom_right[1] - top_left[1]

            rect = pygame.Rect(top_left, (w, h))
            color = self.colors.get(zone, (200, 200, 200))
            pygame.draw.rect(screen, color, rect, self.border_width)
