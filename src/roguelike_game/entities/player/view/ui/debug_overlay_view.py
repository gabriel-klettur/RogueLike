"""
Dibuja hitbox y línea de puntería en modo DEBUG.
"""
import pygame
from src.roguelike_engine.utils.debug import draw_player_aim_line

class DebugOverlayView:
    def render(self, screen, camera, model):
        # hitbox
        rect = model.hitbox()
        scaled = pygame.Rect(camera.apply(rect.topleft), camera.scale((rect.width, rect.height)))
        pygame.draw.rect(screen,(0,255,0), scaled,2)
        # línea de puntería
        draw_player_aim_line(screen, camera, model)