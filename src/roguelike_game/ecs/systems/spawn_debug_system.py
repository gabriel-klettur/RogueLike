import pygame
import roguelike_engine.config.config as config
from roguelike_engine.config.config_tiles import TILE_SIZE

class SpawnDebugSystem:
    """Dibuja marcadores de spawn de NPCs cuando DEBUG=True."""
    def update(self, world, screen, camera):
        if not config.DEBUG or not hasattr(world, 'spawn_tiles'):
            return
        for tx, ty, eid in world.spawn_tiles:
            x, y = tx * TILE_SIZE, ty * TILE_SIZE
            px, py = camera.apply((x, y))
            size = int(TILE_SIZE * camera.zoom)
            rect = pygame.Rect(px, py, size, size)
            pygame.draw.rect(screen, (255, 0, 0), rect, 2)
            font_size = max(8, size // 2)
            font = pygame.font.SysFont(None, font_size)
            text_surf = font.render(str(eid), True, (255, 0, 0))
            text_rect = text_surf.get_rect(center=rect.center)
            screen.blit(text_surf, text_rect)
