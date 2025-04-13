# roguelike_project/systems/combat/effects/pixel_fire.py

import pygame
import random
import math
import time

CELL_SIZE = 6
FLAME_COLOR_DEPTH = 24

# Gradient inspired by: http://fabiensanglard.net/doom_fire_psx/
FLAME_COLOR_PALETTE = [
    (230, 230, 250),  # lavender
    (255, 255, 0),    # yellow
    (255, 215, 0),    # gold
    (255, 105, 180),  # hotpink
    (255, 99, 71),    # tomato
    (72, 61, 139),    # darkslateblue
    (34, 34, 34),     # dark gray (as base)
]

SPREAD_FROM = [
    'bottom'] * 10 + ['left'] * 2 + ['right'] * 2 + ['top']

class FirePixel:
    cached_surfaces = {}

    def __init__(self, x, y, idx, palette):
        self.x = x
        self.y = y
        self.idx = idx
        self.palette = palette
        self.sides = {}
        self.x_offset = 0
        self.render_color = self._generate_render_color()

    def set_sides(self, top=None, left=None, bottom=None, right=None):
        self.sides = {
            "top": top,
            "left": left,
            "bottom": bottom,
            "right": right
        }

    def _generate_render_color(self):
        r, g, b = self.palette[self.idx]
        r = max(0, min(255, r + random.randint(-10, 10)))
        g = max(0, min(255, g + random.randint(-10, 10)))
        b = max(0, min(255, b + random.randint(-10, 10)))
        return (r, g, b)

    def update(self):
        direction = random.choice(SPREAD_FROM)
        neighbor = self.sides.get(direction)

        if neighbor and neighbor.idx < self.idx:
            self.idx = min(len(self.palette) - 1, neighbor.idx + random.randint(-1, 4))
        else:
            self.idx += 1

        self.idx = max(0, min(self.idx, len(self.palette) - 1))

        self.x_offset = math.sin(time.time() * 10 + self.y) * 1.5
        self.render_color = self._generate_render_color()

    def render(self, screen, camera):
        if not camera.is_in_view(self.x, self.y, (CELL_SIZE, CELL_SIZE)):
            return

        r, g, b = self.render_color
        alpha = max(0, 255 * (1 - self.idx / (len(self.palette) - 1)))
        key = (r, g, b, int(alpha))

        if key not in FirePixel.cached_surfaces:
            surf = pygame.Surface((CELL_SIZE, CELL_SIZE), pygame.SRCALPHA)
            surf.fill((r, g, b, int(alpha)))
            FirePixel.cached_surfaces[key] = surf

        pos = camera.apply((self.x + self.x_offset, self.y))
        screen.blit(FirePixel.cached_surfaces[key], pos)


class PixelFireEffect:
    def __init__(self, x, y, width=256, height=256, max_duration=5):
        self.palette = self.generate_gradient(FLAME_COLOR_DEPTH, FLAME_COLOR_PALETTE)
        self.columns = width // CELL_SIZE
        self.rows = height // CELL_SIZE
        self.pixels = []
        self.width = width
        self.height = height
        self.x = x - width // 2  # Centrado respecto al mouse
        self.y = y - height // 2 - 90  # Ajuste manual eje Y para apariencia de suelo

        self.creation_time = time.time()
        self.max_duration = max_duration  # segundos

        self._create_pixels()

    def _is_within_fire_shape(self, col, row):
        cx = self.columns // 2
        cy = self.rows // 2
        dx = col - cx
        dy = row - cy
        dist = math.sqrt(dx ** 2 + dy ** 2)
        radius = min(self.columns, self.rows) // 2
        noise = random.uniform(-1, 1)
        return dist + noise * 2 < radius

    def _create_pixels(self):
        self.pixels = []
        for row in range(self.rows):
            pixel_row = []
            for col in range(self.columns):
                if not self._is_within_fire_shape(col, row):
                    pixel_row.append(None)
                    continue

                idx = 0 if row >= self.rows - 2 else len(self.palette) - 1
                fx = self.x + col * CELL_SIZE
                fy = self.y + row * CELL_SIZE
                pixel = FirePixel(fx, fy, idx, self.palette)
                pixel_row.append(pixel)
            self.pixels.append(pixel_row)

        for r in range(self.rows):
            for c in range(self.columns):
                p = self.pixels[r][c]
                if p is None:
                    continue
                p.set_sides(
                    top=self.pixels[r - 1][c] if r > 0 else None,
                    left=self.pixels[r][c - 1] if c > 0 else None,
                    bottom=self.pixels[r + 1][c] if r < self.rows - 1 else None,
                    right=self.pixels[r][c + 1] if c < self.columns - 1 else None
                )

    def generate_gradient(self, size, stops):
        gradient = []
        for i in range(size):
            t = i / (size - 1)
            for j in range(len(stops) - 1):
                if t <= (j + 1) / (len(stops) - 1):
                    frac = (t * (len(stops) - 1)) - j
                    c1 = stops[j]
                    c2 = stops[j + 1]
                    r = int(c1[0] + frac * (c2[0] - c1[0]))
                    g = int(c1[1] + frac * (c2[1] - c1[1]))
                    b = int(c1[2] + frac * (c2[2] - c1[2]))
                    gradient.append((r, g, b))
                    break
        return gradient

    def update(self):
        if not self.is_empty():
            for row in self.pixels:
                for p in row:
                    if p:
                        p.update()

    def render(self, screen, camera):
        if not self.is_empty():
            for row in self.pixels:
                for p in row:
                    if p:
                        p.render(screen, camera)

    def is_empty(self):
        return time.time() - self.creation_time > self.max_duration