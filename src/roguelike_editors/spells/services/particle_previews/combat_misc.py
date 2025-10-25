from __future__ import annotations

import math
import random
from typing import Tuple

import pygame

from .base import eval_curve, eval_color_gradient


class ParticlePreviewExplosion:
    """Small center explosion loop with optional color palette.

    Parameters:
    - color: base color if palette not provided
    - palette: optional list of colors to pick per particle
    - count: number of particles to spawn per burst
    - speed_range: (min,max) speed for radial particles
    """

    def __init__(
        self,
        color: Tuple[int, int, int] = (255, 180, 80),
        palette: list[Tuple[int, int, int]] | None = None,
        count: int = 24,
        speed_range: tuple[float, float] = (0.8, 2.5),
        *,
        blend_mode: str | None = None,
        size_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        alpha_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        color_over_life: list[list] | list[tuple] | None = None,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._palette = palette
        self._count = max(6, int(count))
        lo, hi = speed_range
        self._spd_lo = float(min(lo, hi))
        self._spd_hi = float(max(lo, hi))
        self._parts: list[tuple[float, float, float, float, int]] = []
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        self._life_frames = 30

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._parts.clear()

    def _spawn(self, w: int, h: int) -> None:
        cx, cy = w / 2.0, h / 2.0
        for _ in range(self._count):
            ang = random.random() * 2 * math.pi
            spd = random.uniform(self._spd_lo, self._spd_hi)
            dx, dy = math.cos(ang) * spd, math.sin(ang) * spd
            self._parts.append((cx, cy, dx, dy, 0))

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        if not self._parts:
            self._spawn(w, h)
        self._surf.fill((0, 0, 0, 0))
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            new_parts: list[tuple[float, float, float, float, int]] = []
            for (x, y, dx, dy, age) in self._parts:
                x += dx
                y += dy
                age += 1
                if age < 30:
                    new_parts.append((x, y, dx, dy, age))
            self._parts = new_parts
            if not self._parts:
                self._spawn(w, h)
            self._acc_ms -= self._step_ms
        for (x, y, dx, dy, age) in self._parts:
            t = max(0.0, min(1.0, age / max(1, self._life_frames)))
            base_sz = 3.0
            if self._size_curve is not None:
                scale = max(0.05, eval_curve(self._size_curve, t, 1.0))
                sz = max(1, int(base_sz * scale))
            else:
                sz = int(base_sz)
            if self._alpha_curve is not None:
                alpha = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                alpha = max(0, 220 - age * 7)
            if self._color_grad is not None:
                col = eval_color_gradient(self._color_grad, t, self._color)
            elif self._palette and len(self._palette) > 0:
                col = random.choice(self._palette)
            else:
                col = self._color
            dot = pygame.Surface((sz, sz), pygame.SRCALPHA)
            dot.fill((*col, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                if self._blend_add:
                    self._surf.blit(dot, (ix, iy), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(dot, (ix, iy))
        return self._surf


class ParticlePreviewTeleport:
    """Expanding/fading ring preview matching in-game TeleportView.

    Cycles between an 'out' and 'in' phase so the preview loops in the cell.
    """

    def __init__(self, color: Tuple[int, int, int] = (0, 200, 255), cycle_ms: int = 600) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._elapsed_ms = 0
        self._cycle_ms = max(200, int(cycle_ms))
        self._phase = 'out'

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        self._surf.fill((0, 0, 0, 0))
        w, h = self._size
        self._elapsed_ms += max(0, dt_ms)
        if self._elapsed_ms >= self._cycle_ms:
            self._elapsed_ms -= self._cycle_ms
            self._phase = 'in' if self._phase == 'out' else 'out'
        t = max(0.0, min(1.0, self._elapsed_ms / max(1, self._cycle_ms)))
        max_radius = max(6, int(min(w, h) * 0.45))
        if self._phase == 'out':
            radius = max(2, int(max_radius * t))
            alpha = max(0, min(255, int(255 * (1 - t))))
        else:
            radius = max(2, int(max_radius * (1 - t)))
            alpha = max(0, min(255, int(255 * t)))
        col = (*self._color, alpha)
        cx, cy = w // 2, h // 2
        try:
            pygame.draw.circle(self._surf, col, (cx, cy), radius, width=4)
        except Exception:
            r = max(1, min(radius, max(1, min(w, h) // 2 - 1)))
            pygame.draw.circle(self._surf, col, (cx, cy), r, width=3)
        return self._surf
