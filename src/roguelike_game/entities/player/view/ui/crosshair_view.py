"""
Renderiza la mira de ratón.
"""
import pygame

class CrosshairView:
    def render(self, screen, camera):
        mx,my = pygame.mouse.get_pos()
        wx = mx/camera.zoom + camera.offset_x
        wy = my/camera.zoom + camera.offset_y
        sx,sy = camera.apply((wx,wy))
        size=10
        pygame.draw.line(screen,(255,0,0),(sx-size,sy),(sx+size,sy),2)
        pygame.draw.line(screen,(255,0,0),(sx,sy-size),(sx,sy+size),2)