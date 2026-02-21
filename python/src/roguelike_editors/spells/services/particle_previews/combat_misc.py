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


class ParticlePreviewPortal:
    """Stylized oval portal with rim, dark core and inner swirl.

    Parameters (all optional with sensible defaults):
    - rim_color, core_color, swirl_color: RGB tuples
    - ellipse_ratio: vertical squash/stretch factor (>1 = taller)
    - outer_radius, inner_radius: control rim thickness
    - swirl_width: pixels for the inner arc thickness
    - chips_count: small exterior chips count
    - angle_speed: radians/sec for subtle swirl motion
    """

    def __init__(
        self,
        rim_color: Tuple[int, int, int] = (180, 255, 120),
        core_color: Tuple[int, int, int] = (16, 36, 28),
        swirl_color: Tuple[int, int, int] = (150, 255, 100),
        *,
        ellipse_ratio: float = 1.8,
        outer_radius: int = 28,
        inner_radius: int = 14,
        swirl_width: int = 6,
        chips_count: int = 4,
        angle_speed: float = 0.8,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._rim = rim_color
        self._core = core_color
        self._swirl = swirl_color
        self._er = float(max(0.5, min(3.0, ellipse_ratio)))
        self._ro = int(max(8, outer_radius))
        self._ri = int(max(2, min(inner_radius, self._ro - 2)))
        self._sw = int(max(2, swirl_width))
        self._chips = int(max(0, chips_count))
        self._ang = 0.0
        self._spd = float(max(0.0, angle_speed))

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._surf.fill((0, 0, 0, 0))
        self._ang += max(0, dt_ms) * 0.001 * self._spd
        cx, cy = w // 2, h // 2
        rx = self._ro
        ry = int(self._ro * self._er)
        # 1) Core dark fill (inner oval)
        try:
            core_rect = pygame.Rect(cx - self._ri, cy - int(self._ri * self._er), self._ri * 2, int(self._ri * self._er) * 2)
            pygame.draw.ellipse(self._surf, (*self._core, 220), core_rect)
        except Exception:
            pass
        # 2) Rim outline (outer oval minus inner)
        try:
            outer_rect = pygame.Rect(cx - rx, cy - ry, rx * 2, ry * 2)
            pygame.draw.ellipse(self._surf, (*self._rim, 255), outer_rect, width=max(2, self._ro - self._ri))
        except Exception:
            pass
        # 3) Inner swirl arc (draw small rectangles along an arc inside the oval)
        try:
            arc_r = (self._ri + self._ro) * 0.5
            rx_i = int(arc_r)
            ry_i = int(arc_r * self._er)
            base = self._ang
            for i in range(20):
                t = base + (i / 20.0) * math.pi * 0.9
                x = int(cx + rx_i * math.cos(t))
                y = int(cy + ry_i * math.sin(t))
                rect = pygame.Rect(x - self._sw // 2, y - self._sw // 2, self._sw, self._sw)
                pygame.draw.rect(self._surf, (*self._swirl, 240), rect)
        except Exception:
            pass
        # 4) Exterior chips
        try:
            for i in range(self._chips):
                a = self._ang * 1.5 + (i / max(1, self._chips)) * 2 * math.pi
                rr = self._ro + 6 + (i % 2) * 3
                x = int(cx + rr * math.cos(a))
                y = int(cy + int(rr * self._er) * math.sin(a))
                s = 3 if (i % 3) else 4
                pygame.draw.rect(self._surf, (*self._rim, 230), pygame.Rect(x - s // 2, y - s // 2, s, s))
        except Exception:
            pass
        return self._surf
