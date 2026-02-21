from __future__ import annotations

import pygame
import random
from typing import Tuple

from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import (
    SmokeEmitterModel,
    SmokeParticle,
)

from .base import DummyCamera, TextureFlipbookHelper, eval_curve, eval_color_gradient


class ParticlePreviewSmoke:
    def __init__(
        self,
        color=(200, 200, 200),
        emit_rate: int = 2,
        warm_start_steps: int = 10,
        *,
        palette: list[tuple[int, int, int]] | None = None,
        speed: float = 1.0,
        lifespan: int | float = 100.0,
        size_range: tuple[int, int] | list[int] | None = None,
        dispersion: float = 0.3,
        gravity: tuple[float, float] | list[float] | None = None,
        drag: float | None = None,
        blend_mode: str | None = None,
        size_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        alpha_over_life: list[list[float]] | list[tuple[float, float]] | None = None,
        color_over_life: list[list] | list[tuple] | None = None,
        texture_path: str | None = None,
        flipbook: dict | None = None,
        speed_variance: float | int | None = None,
        lifetime_jitter: float | int | None = None,
        size_start: int | float | list | tuple | None = None,
    ) -> None:
        if isinstance(size_range, (list, tuple)) and len(size_range) >= 2:
            smin = max(1, int(size_range[0]))
            smax = max(smin, int(size_range[1]))
            sr = (smin, smax)
        else:
            sr = (8, 16)
        self.model = SmokeEmitterModel(
            0,
            0,
            color=color,
            emit_rate=emit_rate,
            speed=float(speed),
            lifespan=float(lifespan),
            size_range=sr,
            dispersion=float(dispersion),
            colors_palette=palette,
        )
        self._life0: float = float(lifespan) if isinstance(lifespan, (int, float)) else 100.0
        self._camera = DummyCamera()
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._warm_started = False
        self._warm_steps = max(0, int(warm_start_steps))
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._blend_premul = isinstance(blend_mode, str) and blend_mode.lower() == "premultiplied_alpha"
        self._tex = TextureFlipbookHelper(texture_path=texture_path, flipbook=flipbook)
        if isinstance(gravity, (list, tuple)) and len(gravity) >= 2:
            self._gravity = (float(gravity[0]), float(gravity[1]))
        else:
            self._gravity = (0.0, 0.0)
        try:
            dval = float(drag) if isinstance(drag, (int, float)) else 0.0
        except Exception:
            dval = 0.0
        self._drag = max(0.0, min(0.98, dval))
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        try:
            self._speed_var = float(speed_variance) if isinstance(speed_variance, (int, float)) else 0.0
        except Exception:
            self._speed_var = 0.0
        try:
            self._life_jitter = float(lifetime_jitter) if isinstance(lifetime_jitter, (int, float)) else 0.0
        except Exception:
            self._life_jitter = 0.0
        self._size_start = size_start

    def _apply_init_customization(self) -> None:
        var = max(-0.95, min(0.95, float(self._speed_var))) if isinstance(self._speed_var, (int, float)) else 0.0
        for p in list(getattr(self.model, 'particles', []) or []):
            if getattr(p, '_rl_tag', False):
                continue
            try:
                if var != 0.0:
                    vx = getattr(p.velocity, 'x', 0.0); vy = getattr(p.velocity, 'y', 0.0)
                    if (vx * vx + vy * vy) > 0.0:
                        k = (1.0 + random.uniform(-var, var))
                        p.velocity.x = vx * k
                        p.velocity.y = vy * k
                lj = float(self._life_jitter) if isinstance(self._life_jitter, (int, float)) else 0.0
                if lj != 0.0:
                    try:
                        base = int(getattr(p, 'lifespan', 60))
                        jit = int(abs(lj) * base) if 0.0 < abs(lj) < 1.0 else int(abs(lj))
                        delta = random.randint(-jit, jit)
                        nl = max(4, base + delta)
                        p.lifespan = nl
                    except Exception:
                        pass
                ss = self._size_start
                if isinstance(ss, (int, float)):
                    p.size = max(1, int(ss))
                elif isinstance(ss, (list, tuple)) and ss:
                    try:
                        p.size = max(1, int(sum(float(v) for v in ss[:2]) / min(2, len(ss))))
                    except Exception:
                        pass
            except Exception:
                pass
            try:
                p._rl_tag = True
            except Exception:
                pass

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._warm_started = False

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self.model.origin.update(w / 2.0, max(4, h - 6))
        if not self._warm_started:
            for _ in range(self._warm_steps):
                self.model.update()
                if (self._gravity != (0.0, 0.0)) or (self._drag > 0.0):
                    gx, gy = self._gravity
                    for p in self.model.particles:
                        if self._gravity != (0.0, 0.0):
                            p.velocity.x += gx
                            p.velocity.y += gy
                        if self._drag > 0.0:
                            p.velocity.x *= (1.0 - self._drag)
                            p.velocity.y *= (1.0 - self._drag)
            self._warm_started = True
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self.model.update()
            if (self._gravity != (0.0, 0.0)) or (self._drag > 0.0):
                gx, gy = self._gravity
                for p in self.model.particles:
                    if self._gravity != (0.0, 0.0):
                        p.velocity.x += gx
                        p.velocity.y += gy
                    if self._drag > 0.0:
                        p.velocity.x *= (1.0 - self._drag)
                        p.velocity.y *= (1.0 - self._drag)
            self._acc_ms -= self._step_ms
            self._apply_init_customization()
        self._surf.fill((0, 0, 0, 0))
        self._apply_init_customization()
        for p in self.model.particles:
            if p.is_dead():
                continue
            t = 1.0 - max(0.0, min(1.0, (p.lifespan / max(1e-3, self._life0))))
            scale = eval_curve(self._size_curve, t, 1.0) if self._size_curve is not None else 1.0
            sz = max(1, int(max(1.0, float(p.size)) * max(0.05, scale)))
            if self._alpha_curve is not None:
                alpha = max(0, min(255, int(255.0 * max(0.0, min(1.0, eval_curve(self._alpha_curve, t, 1.0))))))
            else:
                alpha = max(0, min(255, int(p.lifespan * 2.55)))
            col = eval_color_gradient(self._color_grad, t, p.color) if self._color_grad is not None else p.color
            x, y = self._camera.apply((p.pos.x, p.pos.y))
            if 0 <= x < w and 0 <= y < h:
                frm = self._tex._get_frame(t, sz)
                if frm is not None:
                    try:
                        if col is not None:
                            tint = pygame.Surface(frm.get_size(), pygame.SRCALPHA)
                            tint.fill((*col, 255))
                            frm = frm.copy()
                            frm.blit(tint, (0, 0), special_flags=pygame.BLEND_MULT)
                    except Exception:
                        pass
                    if getattr(self, '_blend_premul', False):
                        try:
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
                        self._surf.blit(frm, (x, y), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(frm, (x, y))
                else:
                    blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
                    blob.fill((*col, alpha))
                    if self._blend_add:
                        self._surf.blit(blob, (x, y), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(blob, (x, y))
        return self._surf


class ParticlePreviewSmokeBurst:
    def __init__(
        self,
        color=(200, 200, 200),
        count: int = 12,
        direction: tuple[float, float] | None = None,
        warm_start_steps: int = 6,
        *,
        blend_mode: str | None = None,
        texture_path: str | None = None,
        flipbook: dict | None = None,
    ) -> None:
        self._color = color
        self._count = max(1, int(count))
        dx, dy = direction if isinstance(direction, (list, tuple)) and len(direction) >= 2 else (0.0, -1.0)
        self._dir = pygame.math.Vector2(float(dx), float(dy))
        if self._dir.length_squared() == 0:
            self._dir = pygame.math.Vector2(0.0, -1.0)
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._parts: list[SmokeParticle] = []
        self._warm_started = False
        self._warm_steps = max(0, int(warm_start_steps))
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._blend_premul = isinstance(blend_mode, str) and blend_mode.lower() == "premultiplied_alpha"
        self._tex = TextureFlipbookHelper(texture_path=texture_path, flipbook=flipbook)

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._parts.clear()
            self._warm_started = False

    def _spawn_burst(self, w: int, h: int) -> None:
        ox, oy = w / 2.0, max(4, h - 6)
        base = self._dir
        for _ in range(self._count):
            p = SmokeParticle(ox, oy, self._color)
            dv = pygame.math.Vector2(base.x + random.gauss(0, 0.25), base.y + random.gauss(0, 0.25))
            if dv.length_squared() == 0:
                dv = pygame.math.Vector2(0, -1)
            dv = dv.normalize() * random.uniform(0.5, 1.5)
            p.apply_force(dv)
            self._parts.append(p)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        if not self._parts:
            self._spawn_burst(w, h)
            self._warm_started = False
        if not self._warm_started:
            for _ in range(self._warm_steps):
                for p in self._parts:
                    p.update()
                self._parts = [p for p in self._parts if not p.is_dead()]
                if not self._parts:
                    self._spawn_burst(w, h)
            self._warm_started = True
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            for p in self._parts:
                p.update()
            self._parts = [p for p in self._parts if not p.is_dead()]
            if not self._parts:
                self._spawn_burst(w, h)
            self._acc_ms -= self._step_ms
        self._surf.fill((0, 0, 0, 0))
        for p in self._parts:
            if p.is_dead():
                continue
            alpha = max(0, min(255, int(p.lifespan * 2.55)))
            sz = max(1, int(p.size))
            x, y = int(p.pos.x), int(p.pos.y)
            if 0 <= x < w and 0 <= y < h:
                frm = self._tex._get_frame(1.0 - max(0.0, min(1.0, p.lifespan / 100.0)), sz)
                if frm is not None:
                    try:
                        tint = pygame.Surface(frm.get_size(), pygame.SRCALPHA)
                        tint.fill((*p.color, 255))
                        frm = frm.copy()
                        frm.blit(tint, (0, 0), special_flags=pygame.BLEND_MULT)
                    except Exception:
                        pass
                    if self._blend_premul:
                        try:
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
                        self._surf.blit(frm, (x, y), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(frm, (x, y))
                else:
                    blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
                    blob.fill((*p.color, alpha))
                    if self._blend_add:
                        self._surf.blit(blob, (x, y), special_flags=pygame.BLEND_ADD)
                    else:
                        self._surf.blit(blob, (x, y))
        return self._surf
