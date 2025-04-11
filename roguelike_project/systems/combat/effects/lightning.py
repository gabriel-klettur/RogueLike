# roguelike_project/systems/combat/effects/lightning.py

import pygame
import random

class Lightning:
    def __init__(self, start_pos, end_pos, lifetime=8, thickness=2, color=None):
        self.start_pos = start_pos
        self.end_pos = end_pos
        self.lifetime = lifetime
        self.max_lifetime = lifetime  # Para calcular la opacidad dinámica
        self.thickness = thickness
        self.base_color = color or (100, 200, 255)
        self.points = self.generate_points(start_pos, end_pos)

    def generate_points(self, start, end, segments=10, offset=15):
        points = [start]
        for i in range(1, segments):
            t = i / segments
            x = start[0] + (end[0] - start[0]) * t + random.randint(-offset, offset)
            y = start[1] + (end[1] - start[1]) * t + random.randint(-offset, offset)
            points.append((x, y))
        points.append(end)
        return points

    def update(self):
        self.lifetime -= 1

    def render(self, surface, camera):
        if self.lifetime <= 0:
            return None

        # Alpha dinámico
        alpha = int(255 * (self.lifetime / self.max_lifetime))

        # Color eléctrico azul con pequeñas variaciones
        r = random.randint(80, 120)
        g = random.randint(180, 230)
        b = 255

        color = (r, g, b, alpha)

        # Crear una superficie temporal con canal alfa
        temp_surface = pygame.Surface(surface.get_size(), pygame.SRCALPHA)

        screen_points = [(x - camera.offset_x, y - camera.offset_y) for (x, y) in self.points]
        for i in range(len(screen_points) - 1):
            pygame.draw.line(temp_surface, color, screen_points[i], screen_points[i + 1], self.thickness)

        # Dibujar la superficie temporal sobre la principal
        surface.blit(temp_surface, (0, 0))

        return surface.get_rect()  # Puedes mejorar esto con un cálculo de dirty rect real si es necesario
