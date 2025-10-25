from __future__ import annotations

import math
import random
from typing import Tuple

import pygame

from .base import TextureFlipbookHelper, eval_curve, eval_color_gradient


class ParticlePreviewHealingAura:
    def __init__(
        self,
        color: Tuple[int, int, int] = (80, 200, 120),
        palette: list[Tuple[int, int, int]] | None = None,
        radius: int | None = None,
        emit_rate: int = 3,
        speed: float = 1.0,
        lifespan: int = 60,
        size_range: tuple[int, int] | list[int] | None = (4, 8),
        warm_start_steps: int = 10,
        *,
        blend_mode: str | None = None,
        size_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        alpha_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        color_over_life: list[list] | list[tuple] | None = None,
        emission_shape: str | None = None,
        emission_extent: tuple | list | int | float | None = None,
        emission_direction: tuple | list | None = None,
        emission_angle_spread_deg: float | int | None = None,
        speed_variance: float | int | None = None,
        lifetime_jitter: float | int | None = None,
        size_start: int | float | list | tuple | None = None,
        bursts: list | tuple | None = None,
        texture_path: str | None = None,
        flipbook: dict | None = None,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._palette = palette
        self._radius = radius
        self._emit = max(1, int(emit_rate))
        self._speed = float(speed)
        self._lifespan = int(lifespan)
        if isinstance(size_range, (list, tuple)) and len(size_range) >= 2:
            self._smin = max(1, int(size_range[0]))
            self._smax = max(self._smin, int(size_range[1]))
        else:
            self._smin, self._smax = 4, 8
        self._parts: list[tuple[float, float, float, float, int, int, int, Tuple[int, int, int]]] = []
        self._warm_started = False
        self._warm_steps = max(0, int(warm_start_steps))
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._blend_premul = isinstance(blend_mode, str) and blend_mode.lower() == "premultiplied_alpha"
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        self._emit_shape = str(emission_shape).lower() if isinstance(emission_shape, str) else None
        self._emit_extent = emission_extent
        if isinstance(emission_direction, (list, tuple)) and len(emission_direction) >= 2:
            try:
                bx, by = float(emission_direction[0]), float(emission_direction[1])
            except Exception:
                bx, by = 0.0, -1.0
        else:
            bx, by = 0.0, -1.0
        base = pygame.math.Vector2(bx, by)
        if base.length_squared() == 0:
            base = pygame.math.Vector2(0.0, -1.0)
        self._emit_dir = base.normalize()
        try:
            self._emit_spread_deg = float(emission_angle_spread_deg) if isinstance(emission_angle_spread_deg, (int, float)) else 0.0
        except Exception:
            self._emit_spread_deg = 0.0
        try:
            self._speed_var = float(speed_variance) if isinstance(speed_variance, (int, float)) else 0.0
        except Exception:
            self._speed_var = 0.0
        try:
            self._life_jitter = float(lifetime_jitter) if isinstance(lifetime_jitter, (int, float)) else 0.0
        except Exception:
            self._life_jitter = 0.0
        self._size_start = size_start
        self._burst_events: list[tuple[int, int]] = []
        self._burst_loop: bool = False
        if isinstance(bursts, (list, tuple)):
            for ev in bursts:
                try:
                    if isinstance(ev, dict):
                        t = float(ev.get('time_s', ev.get('time', 0.0)))
                        c = int(ev.get('count', 0))
                        if ev.get('loop') is True:
                            self._burst_loop = True
                    else:
                        t = float(ev[0]); c = int(ev[1])
                    if c > 0 and t >= 0.0:
                        self._burst_events.append((int(t * 1000.0), c))
                except Exception:
                    continue
            self._burst_events.sort(key=lambda x: x[0])
        self._burst_start_ms = 0
        self._burst_cursor = 0
        self._burst_elapsed_ms = 0
        self._tex = TextureFlipbookHelper(texture_path=texture_path, flipbook=flipbook)

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._parts.clear()
            self._warm_started = False

    def _ellipse_dims(self, w: int, h: int) -> tuple[int, int, int, int]:
        cx, cy = w // 2, h // 2
        if isinstance(self._radius, int):
            max_r = max(8, min(w, h) // 2 - 4)
            r = max(8, min(max_r, self._radius))
            hw = hh = r
        else:
            hw = max(12, min(w, h) // 3)
            hh = max(12, min(w, h) // 3)
        top = cy - hh
        bottom = cy + hh
        return cx, cy, hw, hh, top, bottom

    def _spawn(self, w: int, h: int) -> None:
        cx, cy, hw, hh, top, bottom = self._ellipse_dims(w, h)
        for _ in range(self._emit):
            dx = dy = 0.0
            shape = self._emit_shape
            if shape == "point":
                x = float(cx)
                y = float(cy)
            elif shape == "line":
                if isinstance(self._emit_extent, (int, float)):
                    span = float(self._emit_extent)
                    if 0.0 < span <= 1.0:
                        span = 2.0 * hw * span
                else:
                    span = 2.0 * hw
                half_span = max(2.0, span / 2.0)
                x = cx + random.uniform(-half_span, half_span)
                y = cy
            elif shape == "box":
                ex = ey = None
                if isinstance(self._emit_extent, (list, tuple)) and len(self._emit_extent) >= 2:
                    try:
                        ex = float(self._emit_extent[0])
                        ey = float(self._emit_extent[1])
                    except Exception:
                        ex = ey = None
                bx = (ex / 2.0) if ex else hw
                by = (ey / 2.0) if ey else hh
                dx = random.uniform(-bx, bx)
                dy = random.uniform(-by, by)
                x = cx + dx
                y = max(top, min(bottom, cy + dy))
            elif shape == "circle":
                if isinstance(self._emit_extent, (int, float)):
                    r = float(self._emit_extent)
                elif isinstance(self._emit_extent, (list, tuple)) and len(self._emit_extent) >= 1:
                    r = float(self._emit_extent[0])
                else:
                    r = float(min(hw, hh))
                ang = random.uniform(0.0, 2 * math.pi)
                rr = random.uniform(0.0, r)
                x = cx + math.cos(ang) * rr
                y = cy + math.sin(ang) * rr
                y = max(top, min(bottom, y))
            elif shape == "ring":
                if isinstance(self._emit_extent, (list, tuple)) and len(self._emit_extent) >= 2:
                    rin = max(0.0, float(self._emit_extent[0]))
                    rout = max(rin, float(self._emit_extent[1]))
                else:
                    rin = float(min(hw, hh) * 0.6)
                    rout = float(min(hw, hh))
                ang = random.uniform(0.0, 2 * math.pi)
                rr = random.uniform(rin, rout)
                x = cx + math.cos(ang) * rr
                y = cy + math.sin(ang) * rr
                y = max(top, min(bottom, y))
            elif shape == "cone":
                if isinstance(self._emit_extent, (int, float)):
                    radius = float(self._emit_extent)
                elif isinstance(self._emit_extent, (list, tuple)) and len(self._emit_extent) >= 1:
                    radius = float(self._emit_extent[0])
                else:
                    radius = float(min(hw, hh) * 0.6)
                base = pygame.math.Vector2(self._emit_dir.x, self._emit_dir.y)
                spr = math.radians(self._emit_spread_deg if self._emit_spread_deg else 30.0)
                ang = random.uniform(-spr, spr)
                ca = math.cos(ang); sa = math.sin(ang)
                vx0 = base.x * ca - base.y * sa
                vy0 = base.x * sa + base.y * ca
                rr = random.uniform(0.0, radius)
                x = cx + vx0 * rr
                y = cy + vy0 * rr
                y = max(top, min(bottom, y))
            else:
                for _tries in range(8):
                    dx = random.uniform(-hw, hw)
                    dy = random.uniform(-hh, hh)
                    if (dx / hw) ** 2 + (dy / hh) ** 2 <= 1:
                        break
                x = cx + dx
                y = max(top, min(bottom, cy + dy))
            base = pygame.math.Vector2(self._emit_dir.x, self._emit_dir.y)
            spr = math.radians(self._emit_spread_deg)
            if spr > 0.0:
                ang = random.uniform(-spr, spr)
                ca = math.cos(ang)
                sa = math.sin(ang)
                vx0 = base.x * ca - base.y * sa
                vy0 = base.x * sa + base.y * ca
                vdir = pygame.math.Vector2(vx0, vy0)
            else:
                vdir = base
            var = max(-0.95, min(0.95, float(self._speed_var))) if isinstance(self._speed_var, (int, float)) else 0.0
            spd = abs(self._speed) * (1.0 + random.uniform(-var, var))
            if spd <= 0.0:
                spd = 1.0
            vx = vdir.x * spd
            vy = vdir.y * spd
            if vy < 0:
                frames_to_top = int((y - top) / max(0.001, abs(vy)))
            else:
                frames_to_top = self._lifespan
            life = min(self._lifespan, max(8, frames_to_top))
            lj = float(self._life_jitter)
            if lj != 0.0:
                if 0.0 < abs(lj) < 1.0:
                    jit = int(abs(lj) * life)
                else:
                    jit = int(abs(lj))
                delta = random.randint(-jit, jit)
                life = max(4, min(self._lifespan, life + delta))
            if isinstance(self._size_start, (int, float)):
                size = max(1, int(self._size_start))
            elif isinstance(self._size_start, (list, tuple)) and self._size_start:
                try:
                    size = max(1, int(sum(float(v) for v in self._size_start[:2]) / min(2, len(self._size_start))))
                except Exception:
                    size = random.randint(self._smin, self._smax)
            else:
                size = random.randint(self._smin, self._smax)
            col = self._color
            if self._palette:
                col = self._palette[random.randrange(len(self._palette))]
            self._parts.append((x, y, vx, vy, 0, life, size, col))

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        if not self._warm_started:
            for _ in range(self._warm_steps):
                self._spawn(w, h)
                self._parts = [
                    (x + vx, y + vy, vx, vy, age + 1, life, sz, col)
                    for (x, y, vx, vy, age, life, sz, col) in self._parts
                    if age + 1 < life
                ]
            self._warm_started = True
        self._acc_ms += max(0, dt_ms)
        self._burst_elapsed_ms += max(0, dt_ms)
        if self._burst_events:
            last_time = self._burst_events[-1][0]
            if self._burst_loop and last_time > 0:
                while self._burst_elapsed_ms >= last_time:
                    self._burst_elapsed_ms -= last_time
                    self._burst_cursor = 0
            while self._burst_cursor < len(self._burst_events) and self._burst_elapsed_ms >= self._burst_events[self._burst_cursor][0]:
                count = self._burst_events[self._burst_cursor][1]
                if count > 0:
                    orig_emit = self._emit
                    self._emit = count
                    self._spawn(w, h)
                    self._emit = orig_emit
                self._burst_cursor += 1
        while self._acc_ms >= self._step_ms:
            self._spawn(w, h)
            self._parts = [
                (x + vx, y + vy, vx, vy, age + 1, life, sz, col)
                for (x, y, vx, vy, age, life, sz, col) in self._parts
                if age + 1 < life
            ]
            self._acc_ms -= self._step_ms
        self._surf.fill((0, 0, 0, 0))
        for (x, y, vx, vy, age, life, sz, col) in self._parts:
            t = max(0.0, min(1.0, age / max(1, life)))
            if self._size_curve is not None:
                scale = max(0.05, eval_curve(self._size_curve, t, 1.0))
                draw_sz = max(1, int(sz * scale))
            else:
                draw_sz = sz
            if self._alpha_curve is not None:
                alpha = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                alpha = max(0, min(255, int(255 * (1 - t))))
            dcol = eval_color_gradient(self._color_grad, t, col) if self._color_grad is not None else col
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                frm = self._tex._get_frame(t, draw_sz)
                if frm is not None:
                    try:
                        if dcol is not None:
                            import pygame
                            tint = pygame.Surface(frm.get_size(), pygame.SRCALPHA)
                            tint.fill((*dcol, 255))
                            frm = frm.copy()
                            frm.blit(tint, (0, 0), special_flags=pygame.BLEND_MULT)
                    except Exception:
                        pass
                    if getattr(self, '_blend_premul', False):
                        try:
                            import pygame
                            mod = pygame.Surface(frm.get_size(), pygame.SRCALPHA)
                            mod.fill((alpha, alpha, alpha, 255))
                            frm.blit(mod, (0, 0), special_flags=pygame.BLEND_MULT)
                        except Exception:
                            pass
                    else:
                        try:
                            frm.set_alpha(alpha)
                        except Exception:
                            pass
                    if self._blend_add:
                        self._surf.blit(frm, (ix, iy), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(frm, (ix, iy))
                else:
                    import pygame
                    blob = pygame.Surface((draw_sz, draw_sz), pygame.SRCALPHA)
                    blob.fill((*dcol, alpha))
                    if self._blend_add:
                        self._surf.blit(blob, (ix, iy), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(blob, (ix, iy))
        return self._surf
