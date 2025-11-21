from __future__ import annotations

import math
import random
from typing import Tuple

import pygame


class ParticlePreviewAura:
    def __init__(self, color: Tuple[int, int, int] = (120, 255, 180), radius: int | None = None, speed: float = 1.0, count: int = 24, palette: list[Tuple[int, int, int]] | None = None, *, blend_mode: str | None = None, ellipse_ratio: float = 1.0, ring_layers: int = 1, layer_spread: float = 0.3) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._palette = palette
        self._radius = radius
        self._theta = 0.0
        self._speed = speed
        self._count = count
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        try:
            self._ellipse = float(ellipse_ratio)
        except Exception:
            self._ellipse = 1.0
        try:
            self._layers = max(1, int(ring_layers))
        except Exception:
            self._layers = 1
        try:
            self._layer_spread = float(layer_spread)
        except Exception:
            self._layer_spread = 0.3

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._surf.fill((0, 0, 0, 0))
        self._theta += max(0, dt_ms) * 0.001 * self._speed
        if isinstance(self._radius, int):
            max_r = max(8, min(w, h) // 2 - 4)
            radius = max(8, min(max_r, self._radius))
        else:
            radius = max(8, min(w, h) // 3)
        cx, cy = w // 2, h // 2
        er = max(0.25, min(3.0, self._ellipse if isinstance(self._ellipse, (int, float)) else 1.0))
        layers = self._layers if isinstance(getattr(self, "_layers", None), int) else 1
        layers = max(1, layers)
        spread = self._layer_spread if isinstance(getattr(self, "_layer_spread", None), (int, float)) else 0.3
        spread = max(0.0, min(0.9, float(spread)))
        base_radius = float(radius)
        if layers <= 1:
            ring_radii = [base_radius]
        else:
            min_factor = 1.0 - spread
            max_factor = 1.0 + spread
            if layers == 2:
                factors = [min_factor, max_factor]
            else:
                step = (max_factor - min_factor) / float(max(1, layers - 1))
                factors = [min_factor + step * i for i in range(layers)]
            ring_radii = [max(4.0, base_radius * f) for f in factors]
        outer_r = max(ring_radii)
        outer_rx = outer_r
        outer_ry = outer_r * er
        max_rx = max(1, w // 2 - 4)
        max_ry = max(1, h // 2 - 4)
        scale = 1.0
        if outer_rx > max_rx or outer_ry > max_ry:
            scale = min(max_rx / float(outer_rx), max_ry / float(outer_ry))
        scaled_radii = [(int(r * scale), int(r * er * scale)) for r in ring_radii]
        for (rx, ry) in scaled_radii:
            for i in range(self._count):
                t = self._theta + (i / self._count) * (2 * 3.14159)
                x = int(cx + rx * math.cos(t))
                y = int(cy + ry * math.sin(t))
                alpha = 140 + int(100 * (0.5 + 0.5 * math.sin(t * 2)))
                col = self._color
                if self._palette and len(self._palette) > 0:
                    col = self._palette[i % len(self._palette)]
                dot = pygame.Surface((3, 3), pygame.SRCALPHA)
                dot.fill((*col, max(0, min(255, alpha))))
                if 0 <= x < w and 0 <= y < h:
                    self._surf.blit(dot, (x, y))
        return self._surf


class ParticlePreviewDash:
    def __init__(self, color: Tuple[int, int, int] = (180, 220, 255), speed_px: float = 60.0, *, blend_mode: str | None = None) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._pos = 0.0
        self._trail: list[tuple[float, float, int]] = []
        self._speed = speed_px
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._pos = 0.0
            self._trail.clear()

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        self._surf.fill((0, 0, 0, 0))
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self._pos += self._speed * (self._step_ms / 1000.0)
            w_step, h_step = self._size
            px_step = int(self._pos % max(1, w_step - 6)) + 3
            py_step = h_step // 2
            self._trail.append((px_step, py_step, 0))
            self._trail = [(x, y, age + 1) for (x, y, age) in self._trail if age + 1 < 24]
            self._acc_ms -= self._step_ms
        w, h = self._size
        px = int(self._pos % max(1, w - 6)) + 3
        py = h // 2
        for (x, y, age) in self._trail:
            alpha = max(0, 200 - age * 8)
            length = max(1, 8 - age // 3)
            surf = pygame.Surface((length, 2), pygame.SRCALPHA)
            surf.fill((*self._color, alpha))
            x0 = int(x - length)
            if 0 <= x0 < w and 0 <= y < h:
                if self._blend_add:
                    self._surf.blit(surf, (x0, y), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(surf, (x0, y))
        head = pygame.Surface((3, 3), pygame.SRCALPHA)
        head.fill((*self._color, 255))
        if self._blend_add:
            self._surf.blit(head, (px, py), special_flags=pygame.BLEND_ADD)
        else:
            self._surf.blit(head, (px, py))
        return self._surf


class ParticlePreviewSlash:
    def __init__(self, color: Tuple[int, int, int] = (255, 230, 150), speed: float = 2.5, *, blend_mode: str | None = None) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._angle = 0.0
        self._speed = speed
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        self._surf.fill((0, 0, 0, 0))
        w, h = self._size
        cx, cy = w // 2, h // 2
        self._angle += max(0, dt_ms) * 0.001 * self._speed
        radius = max(10, min(w, h) // 3)
        for i in range(14):
            t = self._angle + (i / 14) * (math.pi / 2)
            x = int(cx + radius * math.cos(t))
            y = int(cy + radius * math.sin(t))
            alpha = 220 - i * 12
            dot = pygame.Surface((3, 3), pygame.SRCALPHA)
            dot.fill((*self._color, max(0, alpha)))
            if 0 <= x < w and 0 <= y < h:
                if self._blend_add:
                    self._surf.blit(dot, (x, y), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(dot, (x, y))
        return self._surf


class ParticlePreviewLaser:
    def __init__(self, color: Tuple[int, int, int] = (120, 200, 255), *, blend_mode: str | None = None) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._sparks: list[tuple[int, int, int]] = []
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._sparks.clear()

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            w_step, h_step = self._size
            y_step = h_step // 2
            if random.random() < 0.7:
                self._sparks.append((random.randint(4, max(4, w_step - 5)), y_step + random.randint(-4, 4), 0))
            self._sparks = [(x, sy, age + 1) for (x, sy, age) in self._sparks if age + 1 < 20]
            self._acc_ms -= self._step_ms
        w, h = self._size
        self._surf.fill((0, 0, 0, 0))
        tmp = self._surf if not self._blend_add else pygame.Surface((w, h), pygame.SRCALPHA)
        y = h // 2
        import pygame as _pg
        _pg.draw.rect(tmp, (*self._color, 200), _pg.Rect(2, y - 1, max(1, w - 4), 2))
        for (x, sy, age) in self._sparks:
            alpha = max(0, 200 - age * 10)
            dot = pygame.Surface((2, 2), pygame.SRCALPHA)
            dot.fill((*self._color, alpha))
            tmp.blit(dot, (x, sy))
        if self._blend_add and tmp is not self._surf:
            self._surf.blit(tmp, (0, 0), special_flags=pygame.BLEND_ADD)
        return self._surf
