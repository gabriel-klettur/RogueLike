import pygame
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

class ResurrectionAreaSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        # Mostrar solo si el jugador está muerto (tiene GrayscaleComponent)
        grays = world.components.get('GrayscaleComponent', {})
        if world.player_entity not in grays:
            return
        # Coordenadas del centro de la zona de resurrección
        res_x, res_y = world.map_manager.lobby_offset
        cw, ch = global_map_settings.zone_width, global_map_settings.zone_height
        tx = res_x + cw//2 - 1
        ty = res_y + ch//2 - 1
        wx, wy = tx * TILE_SIZE, ty * TILE_SIZE
        x0, y0 = camera.apply((wx, wy))
        size = TILE_SIZE * 3
        overlay = pygame.Surface((size, size), pygame.SRCALPHA)
        overlay.fill((255, 255, 0, 80))
        screen.blit(overlay, (x0, y0))
        pygame.draw.rect(screen, (255, 255, 0), pygame.Rect(x0, y0, size, size), 3)
