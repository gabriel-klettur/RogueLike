import random
import math
import time
import pygame

from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.palette import (
    FLAME_COLOR_PALETTE, FLAME_COLOR_DEPTH, CELL_SIZE, SPREAD_FROM
)

class FirePixel:
    """Un solo píxel de fuego, con propagación y render cache determinista."""
    # Cache surfaces per palette index
    cached_surfaces: dict[int, pygame.Surface] = {}

    def __init__(self, x: float, y: float, idx: int, palette: list[tuple[int,int,int]], rng: random.Random):
        self.x = x
        self.y = y
        self.idx = idx
        self.palette = palette
        self.rng = rng
        self.sides: dict[str,FirePixel|None] = {}
        self.x_offset = 0.0
        self.render_color = self._generate_render_color()

    def set_sides(self, top=None, left=None, bottom=None, right=None):
        self.sides = {"top": top, "left": left, "bottom": bottom, "right": right}

    def _generate_render_color(self) -> tuple[int,int,int]:
        # Deterministic base color without jitter
        return self.palette[self.idx]

    def update(self, current_time: float):
        # Use seeded RNG and single time for offset
        direction = self.rng.choice(SPREAD_FROM)
        neighbor = self.sides.get(direction)
        if neighbor and neighbor.idx < self.idx:
            self.idx = min(len(self.palette)-1, neighbor.idx + self.rng.randint(-1,4))
        else:
            self.idx += 1
        self.idx = max(0, min(self.idx, len(self.palette)-1))
        self.x_offset = math.sin(current_time * 10 + self.y) * 1.5
        self.render_color = self._generate_render_color()

    def render(self, screen: pygame.Surface, camera):
        if not camera.is_in_view(self.x, self.y, (CELL_SIZE, CELL_SIZE)):
            return
        idx = self.idx
        if idx not in FirePixel.cached_surfaces:
            r, g, b = self.palette[idx]
            alpha = max(0, int(255 * (1 - idx / (len(self.palette)-1))))
            surf = pygame.Surface((CELL_SIZE, CELL_SIZE), pygame.SRCALPHA)
            surf.fill((r, g, b, alpha))
            FirePixel.cached_surfaces[idx] = surf
        pos = camera.apply((self.x + self.x_offset, self.y))
        screen.blit(FirePixel.cached_surfaces[idx], pos)

class ArcaneFlameModel:
    """
    Modelo de fuego arcano basado en PixelFireEffect:
    matriz de FirePixel, posición, tiempo de vida.
    """
    def __init__(
        self,
        x: float,
        y: float,
        width: int = 256,
        height: int = 256,
        max_duration: float = 5.0,
        seed: int = 0
    ):
        self.center_x = x
        self.center_y = y
        self.width = width
        self.height = height
        self.max_duration = max_duration
        self.start_time = time.time()
        self.rng = random.Random(seed)
        self.palette = self._generate_gradient(FLAME_COLOR_DEPTH, FLAME_COLOR_PALETTE)
        self.columns = width // CELL_SIZE
        self.rows = height // CELL_SIZE
        self._create_pixels()
        # Flatten pixels for faster update
        self.pixels_flat = [p for row in self.pixels for p in row if p]

    def _generate_gradient(self, size: int, stops: list[tuple[int,int,int]]) -> list[tuple[int,int,int]]:
        gradient = []
        for i in range(size):
            t = i / (size-1)
            for j in range(len(stops)-1):
                if t <= (j+1)/(len(stops)-1):
                    frac = t*(len(stops)-1) - j
                    c1, c2 = stops[j], stops[j+1]
                    r = int(c1[0] + frac*(c2[0]-c1[0]))
                    g = int(c1[1] + frac*(c2[1]-c1[1]))
                    b = int(c1[2] + frac*(c2[2]-c1[2]))
                    gradient.append((r,g,b))
                    break
        return gradient

    def _create_pixels(self):
        self.pixels: list[list[FirePixel|None]] = []
        cx, cy = self.columns//2, self.rows//2
        radius = min(cx, cy)
        for row in range(self.rows):
            row_list: list[FirePixel|None] = []
            for col in range(self.columns):
                dist = math.hypot(col-cx, row-cy)
                if dist + self.rng.uniform(-1,1)*2 < radius:
                    idx = 0 if row >= self.rows-2 else len(self.palette)-1
                    fx = self.center_x - self.width//2 + col*CELL_SIZE
                    fy = self.center_y - self.height//2 + row*CELL_SIZE
                    pixel = FirePixel(fx, fy, idx, self.palette, self.rng)
                    row_list.append(pixel)
                else:
                    row_list.append(None)
            self.pixels.append(row_list)
        for r in range(self.rows):
            for c in range(self.columns):
                p = self.pixels[r][c]
                if not p: continue
                top = self.pixels[r-1][c] if r>0 else None
                left = self.pixels[r][c-1] if c>0 else None
                bottom = self.pixels[r+1][c] if r<self.rows-1 else None
                right = self.pixels[r][c+1] if c<self.columns-1 else None
                p.set_sides(top=top, left=left, bottom=bottom, right=right)

    def update(self):
        # Single time read and flat iteration
        t = time.time()
        for p in self.pixels_flat:
            p.update(t)

    def is_finished(self) -> bool:
        return (time.time() - self.start_time) > self.max_duration
