import pygame
import random
import math

class Flame:
    def __init__(self, x, y):
        self.base_x = x
        self.base_y = y
        self.height = random.randint(60, 100)
        self.width = random.randint(20, 40)
        self.age = 0
        self.max_age = 30 + random.randint(10, 30)
        self.amplitude = random.uniform(3, 6)
        self.frequency = random.uniform(0.1, 0.2)
        self.color = (255, 165, 0)  # naranja-amarillo
        self.opacity = 255

    def is_alive(self):
        return self.age < self.max_age

    def update(self):
        self.age += 1
        self.opacity = max(0, 255 * (1 - self.age / self.max_age))

    def render(self, surface, camera):
        if not self.is_alive():
            return

        points = []
        segments = 12
        for i in range(segments + 1):
            t = i / segments
            y = self.base_y - t * self.height
            offset = math.sin(self.age * self.frequency + t * math.pi * 2) * self.amplitude * (1 - t)
            x = self.base_x + offset
            screen_x = x - camera.offset_x
            screen_y = y - camera.offset_y
            points.append((screen_x, screen_y))

        if len(points) > 1:
            pygame.draw.lines(surface, (*self.color, int(self.opacity)), False, points, 2)
