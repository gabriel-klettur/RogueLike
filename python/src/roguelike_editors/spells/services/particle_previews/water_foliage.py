from __future__ import annotations

import math
import random
from typing import Tuple

import pygame

from .base import eval_curve, eval_color_gradient


class ParticlePreviewWaterFountain:
    def __init__(
        self,
        color: Tuple[int, int, int] = (100, 180, 255),
        spouts: list[float] | tuple[float, ...] = (0.34, 0.5, 0.66),
        emit_rate: int = 5,
        speed: float = 2.0,
        gravity: float = 0.25,
        droplet_size: int = 2,
        splash_count: int = 2,
        *,
        blend_mode: str | None = None,
        alpha_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        size_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        color_over_life: list[list] | list[tuple] | None = None,
        emission_shape: str | None = None,
        emission_extent: list | tuple | int | float | None = None,
        speed_variance: float | int | None = None,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._spouts = [float(max(0.05, min(0.95, s))) for s in list(spouts)] if spouts else [0.5]
        self._emit = max(1, int(emit_rate))
        self._speed = float(speed)
        self._g = float(gravity)
        self._sz = max(1, int(droplet_size))
        self._splash = max(0, int(splash_count))
        self._drops: list[tuple[float, float, float, float, int, int, int]] = []
        self._spl: list[tuple[float, float, float, float, int, int, int]] = []
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        self._emit_shape = str(emission_shape).lower() if isinstance(emission_shape, str) else None
        self._emit_extent = emission_extent
        try:
            self._speed_var = float(speed_variance) if isinstance(speed_variance, (int, float)) else 0.0
        except Exception:
            self._speed_var = 0.0

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._drops.clear()
            self._spl.clear()

    def _spawn_droplets(self, w: int, h: int) -> None:
        top_y = max(2, int(h * 0.18))
        if self._spouts:
            xs = [int(2 + s * max(1, w - 4)) for s in self._spouts]
        else:
            if isinstance(self._emit_extent, (int, float)):
                if 0.0 < float(self._emit_extent) <= 1.0:
                    span = int(max(4, (w - 4) * float(self._emit_extent)))
                else:
                    span = int(max(4, min(w - 4, float(self._emit_extent))))
            else:
                span = w - 4
            left = 2 + (w - 4 - span) // 2
            step = max(1, span // 4)
            xs = [left + step, left + 2 * step, left + 3 * step]
        var = max(-0.95, min(0.95, float(self._speed_var))) if isinstance(self._speed_var, (int, float)) else 0.0
        for x in xs:
            for _ in range(self._emit):
                vx = random.uniform(-0.2, 0.2) * max(0.5, self._speed * 0.35)
                vy = abs(self._speed) * (1.0 + random.uniform(-var, var)) + random.uniform(-0.2, 0.2)
                size = max(1, int(self._sz + random.choice((-1, 0, 0, 1))))
                life = 120
                self._drops.append((float(x), float(top_y), float(vx), float(vy), size, 0, life))

    def _update_step(self, w: int, h: int) -> None:
        self._spawn_droplets(w, h)
        ground = h - 3
        new_drops: list[tuple[float, float, float, float, int, int, int]] = []
        for (x, y, vx, vy, sz, age, life) in self._drops:
            vy += self._g
            x += vx
            y += vy
            age += 1
            if y >= ground:
                if self._splash > 0:
                    for _ in range(self._splash):
                        ang = random.uniform(-0.9, -2.2)
                        spd = random.uniform(0.8, 1.6) * (0.6 + 0.4 * (sz / max(1, self._sz)))
                        svx = math.cos(ang) * spd
                        svy = math.sin(ang) * spd
                        ssz = max(1, sz - 1)
                        slife = random.randint(10, 24)
                        self._spl.append((x, float(ground), svx, svy, ssz, 0, slife))
                continue
            if age < life and -4 <= x < w + 4 and -4 <= y < h + 6:
                new_drops.append((x, y, vx, vy, sz, age, life))
        self._drops = new_drops
        new_spl: list[tuple[float, float, float, float, int, int, int]] = []
        for (x, y, vx, vy, sz, age, life) in self._spl:
            vy += self._g * 0.8
            x += vx
            y += vy
            age += 1
            if age < life and -4 <= x < w + 4 and -4 <= y < h + 6:
                new_spl.append((x, y, vx, vy, sz, age, life))
        self._spl = new_spl

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self._update_step(w, h)
            self._acc_ms -= self._step_ms
        self._surf.fill((0, 0, 0, 0))
        base = self._color
        try:
            for s in self._spouts:
                x = int(2 + s * max(1, w - 4))
                pygame.draw.line(self._surf, (*base, 40), (x, int(h * 0.18)), (x, h - 3), 1)
        except Exception:
            pass
        for (x, y, vx, vy, sz, age, life) in self._drops:
            t = max(0.0, min(1.0, age / max(1, life)))
            if self._alpha_curve is not None:
                alpha = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                alpha = max(80, min(255, 220 - age))
            draw_sz = sz
            if self._size_curve is not None:
                scale = max(0.05, eval_curve(self._size_curve, t, 1.0))
                draw_sz = max(1, int(sz * scale))
            dcol = eval_color_gradient(self._color_grad, t, base) if self._color_grad is not None else base
            blob = pygame.Surface((draw_sz, draw_sz), pygame.SRCALPHA)
            blob.fill((*dcol, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                if self._blend_add:
                    self._surf.blit(blob, (ix, iy), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(blob, (ix, iy))
        for (x, y, vx, vy, sz, age, life) in self._spl:
            t = max(0.0, min(1.0, age / max(1, life)))
            if self._alpha_curve is not None:
                alpha = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                alpha = max(0, min(255, int(255 * (1 - age / max(1, life)))))
            draw_sz = sz
            if self._size_curve is not None:
                scale = max(0.05, eval_curve(self._size_curve, t, 1.0))
                draw_sz = max(1, int(sz * scale))
            base2 = (min(255, base[0] + 20), min(255, base[1] + 20), min(255, base[2] + 20))
            dcol = eval_color_gradient(self._color_grad, t, base2) if self._color_grad is not None else base2
            blob = pygame.Surface((draw_sz, draw_sz), pygame.SRCALPHA)
            blob.fill((*dcol, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                if self._blend_add:
                    self._surf.blit(blob, (ix, iy), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(blob, (ix, iy))
        return self._surf


class ParticlePreviewFallingLeaf:
    def __init__(
        self,
        color: Tuple[int, int, int] = (140, 200, 80),
        interval_ms: int = 30000,
        life_ms: int = 6000,
        speed: float = 0.5,
        gravity: float = 0.06,
        sway_amp: float = 0.6,
        sway_speed: float = 0.15,
        size: Tuple[int, int] = (3, 2),
        *,
        blend_mode: str | None = None,
        alpha_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        color_over_life: list[list] | list[tuple] | None = None,
        lifetime_jitter: int | float | None = None,
        size_start: int | float | list | tuple | None = None,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._interval = max(1000, int(interval_ms))
        self._life_ms = max(1000, int(life_ms))
        self._base_vy = float(speed)
        self._g = float(gravity)
        self._sway_amp = float(sway_amp)
        self._sway_speed = float(sway_speed)
        self._leaf_w = max(2, int(size[0]))
        self._leaf_h = max(2, int(size[1]))
        self._timer_ms = random.randint(0, self._interval - 1)
        self._acc_ms = 0
        self._step_ms = 33
        self._leaf: tuple[float, float, float, float, float, int] | None = None
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        try:
            self._life_jitter = float(lifetime_jitter) if isinstance(lifetime_jitter, (int, float)) else 0.0
        except Exception:
            self._life_jitter = 0.0
        self._size_start = size_start
        self._leaf_life_ms = self._life_ms

    def _eval_curve(self, curve, t: float, default: float) -> float:
        return eval_curve(curve, t, default)

    def _eval_color_gradient(self, grad, t: float, base: Tuple[int, int, int]) -> Tuple[int, int, int]:
        return eval_color_gradient(grad, t, base)

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._leaf = None

    def _spawn_leaf(self, w: int, h: int) -> None:
        x = random.uniform(w * 0.25, w * 0.75)
        y = random.uniform(h * 0.05, h * 0.25)
        vx = 0.0
        vy = max(0.1, self._base_vy)
        sway_phase = random.random() * 6.28318
        lj = float(self._life_jitter)
        life_ms = self._life_ms
        if lj != 0.0:
            if 0.0 < abs(lj) < 1.0:
                jit = int(abs(lj) * life_ms)
            else:
                jit = int(abs(lj))
            delta = random.randint(-jit, jit)
            life_ms = max(500, min(int(self._life_ms * 2), life_ms + delta))
        self._leaf_life_ms = life_ms
        if isinstance(self._size_start, (int, float)):
            self._leaf_w = max(2, int(self._size_start))
            self._leaf_h = max(2, int(self._size_start))
        elif isinstance(self._size_start, (list, tuple)) and self._size_start:
            try:
                w0 = int(self._size_start[0])
                h0 = int(self._size_start[1] if len(self._size_start) > 1 else self._size_start[0])
                self._leaf_w = max(2, w0)
                self._leaf_h = max(2, h0)
            except Exception:
                pass
        self._leaf = (x, y, vx, vy, sway_phase, 0)

    def _step(self, w: int, h: int, steps: int) -> None:
        self._timer_ms += steps * self._step_ms
        if self._leaf is None and self._timer_ms >= self._interval:
            self._timer_ms %= self._interval
            self._spawn_leaf(w, h)
        if self._leaf is None:
            return
        x, y, vx, vy, phase, age = self._leaf
        for _ in range(steps):
            phase += self._sway_speed
            vx = self._sway_amp * math.sin(phase)
            vy += self._g
            x += vx
            y += vy
            age += self._step_ms
            if y >= h - 2 or age >= self._leaf_life_ms:
                self._leaf = None
                return
        self._leaf = (x, y, vx, vy, phase, age)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._acc_ms += max(0, dt_ms)
        steps = 0
        while self._acc_ms >= self._step_ms:
            self._step(w, h, 1)
            self._acc_ms -= self._step_ms
            steps += 1
        self._surf.fill((0, 0, 0, 0))
        if self._leaf is not None:
            x, y, vx, vy, phase, age = self._leaf
            t = max(0.0, min(1.0, age / max(1, self._leaf_life_ms)))
            if self._alpha_curve is not None:
                a = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                a = max(80, min(255, 255 - int(255 * t)))
            leaf = pygame.Surface((self._leaf_w, self._leaf_h), pygame.SRCALPHA)
            dcol = eval_color_gradient(self._color_grad, t, self._color) if self._color_grad is not None else self._color
            leaf.fill((*dcol, a))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                if self._blend_add:
                    self._surf.blit(leaf, (ix, iy), special_flags=pygame.BLEND_ADD)
                else:
                    self._surf.blit(leaf, (ix, iy))
        return self._surf


class ParticlePreviewWaterFlow:
    def __init__(
        self,
        base_color: Tuple[int, int, int] = (20, 40, 80),
        highlight_color: Tuple[int, int, int] = (60, 110, 160),
        direction: Tuple[float, float] = (1.0, 0.0),
        speed: float = 0.6,
        stripe_gap: int = 8,
        ripple_amp: float = 0.6,
        alpha_base: int = 120,
        alpha_wave: int = 80,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._base = tuple(map(int, base_color))
        self._hl = tuple(map(int, highlight_color))
        self._dir = pygame.math.Vector2(float(direction[0]), float(direction[1]))
        if self._dir.length_squared() == 0:
            self._dir.update(1.0, 0.0)
        self._speed = float(speed)
        self._gap = max(2, int(stripe_gap))
        self._ripple = float(ripple_amp)
        self._ab = max(0, min(255, int(alpha_base)))
        self._aw = max(0, min(255, int(alpha_wave)))

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def _draw_horizontal(self, w: int, h: int, t_ms: int) -> None:
        assert self._surf is not None
        self._surf.fill((*self._base, self._ab))
        px_per_ms = self._speed
        offset = int((t_ms * px_per_ms) % self._gap)
        for x in range(-offset, w + self._gap, self._gap):
            col = (*self._hl, self._aw)
            pygame.draw.rect(self._surf, col, pygame.Rect(x, 0, 2, h))

    def _draw_vertical(self, w: int, h: int, t_ms: int) -> None:
        assert self._surf is not None
        self._surf.fill((*self._base, self._ab))
        px_per_ms = self._speed
        offset = int((t_ms * px_per_ms) % self._gap)
        for y in range(-offset, h + self._gap, self._gap):
            col = (*self._hl, self._aw)
            pygame.draw.rect(self._surf, col, pygame.Rect(0, y, w, 2))

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        t_ms = pygame.time.get_ticks()
        if abs(self._dir.x) >= abs(self._dir.y):
            self._draw_horizontal(w, h, t_ms)
        else:
            self._draw_vertical(w, h, t_ms)
        if self._ripple > 0:
            try:
                ripple = pygame.Surface((w, h), pygame.SRCALPHA)
                if abs(self._dir.x) >= abs(self._dir.y):
                    for y in range(h):
                        a = int(max(0, min(40, 20 + 20 * math.sin((y / max(1, h)) * 6.28318 + (t_ms * 0.002)))))
                        pygame.draw.line(ripple, (*self._hl, a), (0, y), (w, y))
                else:
                    for x in range(w):
                        a = int(max(0, min(40, 20 + 20 * math.sin((x / max(1, w)) * 6.28318 + (t_ms * 0.002)))))
                        pygame.draw.line(ripple, (*self._hl, a), (x, 0), (x, h))
                self._surf.blit(ripple, (0, 0))
            except Exception:
                pass
        return self._surf
