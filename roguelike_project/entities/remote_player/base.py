import pygame

class RemotePlayer:
    def __init__(self, x, y):
        self.x = x
        self.y = y
        self.color = (255, 255, 0)
        self.radius = 20

    def render(self, screen, camera):
        pos = camera.apply((self.x, self.y))
        pygame.draw.circle(screen, self.color, pos, self.radius)