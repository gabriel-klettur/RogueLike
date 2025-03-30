# entities/remote_player/base.py

import pygame

class RemotePlayer:
    def __init__(self, x, y, pid=None):
        self.x = x
        self.y = y
        self.color = (0, 255, 255)
        self.radius = 20
        self.pid = pid

    def render(self, screen, camera):
        pos = camera.apply((self.x, self.y))
        pygame.draw.circle(screen, self.color, pos, self.radius)

        # Dibujar ID encima del jugador (opcional, para debug)
        if self.pid:
            font = pygame.font.SysFont("Arial", 14)
            text = font.render(self.pid[:6], True, (255, 255, 255))
            rect = text.get_rect(center=(pos[0], pos[1] - 30))
            screen.blit(text, rect)
