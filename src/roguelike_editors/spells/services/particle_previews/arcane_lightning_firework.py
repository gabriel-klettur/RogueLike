from __future__ import annotations

import math
import random
from typing import Tuple

import pygame

from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.model import (
    ArcaneFlameModel,
)
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.palette import (
    CELL_SIZE,
)
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.model import (
    FireworkLaunchModel,
)
from roguelike_game.ecs.components.abilities.lightning_model import LightningModel

from .base import DummyCamera, eval_curve, eval_color_gradient


class ParticlePreviewArcaneFlame:
    def __init__(
        self,
        duration: float = 5.0,
        seed: int = 0,
        spark_rate: int = 6,
        spark_speed: float = 1.2,
        spark_size_range: tuple[int, int] = (2, 3),
        spark_lifespan: int = 28,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._model: ArcaneFlameModel | None = None
        self._duration = float(duration)
        self._seed = int(seed)
        self._spark_rate = max(0, int(spark_rate))
        self._spark_speed = float(spark_speed)
        self._spark_sz_min = int(min(spark_size_range))
        self._spark_sz_max = int(max(spark_size_range))
        self._spark_life = max(6, int(spark_lifespan))
        self._sparks: list[tuple[float, float, float, float, int, int, tuple[int, int, int]]] = []
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._model = None

    def _ensure_model(self, w: int, h: int) -> None:
        if self._model is None:
            pad = 4
            s = max(CELL_SIZE, min(w, h) - pad * 2)
            grid = s - (s % CELL_SIZE)
            grid = max(CELL_SIZE, grid)
            cx, cy = w // 2, h // 2
            self._model = ArcaneFlameModel(cx, cy, width=grid, height=grid, max_duration=self._duration, seed=self._seed)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._ensure_model(w, h)
        self._acc_ms += max(0, dt_ms)
        steps = 0
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
            steps += 1
        try:
            if self._model is not None:
                rows = getattr(self._model, 'rows', 0)
                cols = getattr(self._model, 'columns', 0)
                pixels = getattr(self._model, 'pixels', None)
                if pixels and rows > 0 and cols > 0:
                    br = rows - 1
                    for c in range(cols):
                        p = pixels[br][c]
                        if p and random.random() < 0.85:
                            p.idx = 0
                    br2 = rows - 2
                    if br2 >= 0:
                        for c in range(cols):
                            p = pixels[br2][c]
                            if p and random.random() < 0.35:
                                p.idx = min(p.idx, 1)
        except Exception:
            pass
        try:
            if self._spark_rate > 0:
                spawn_n = max(0, int(self._spark_rate * 0.35))
                total_spawns = spawn_n * steps
                cx, cy = w / 2.0, h / 2.0
                palette = getattr(self._model, 'palette', None) if self._model else None
                for _ in range(total_spawns):
                    ang = random.random() * 2 * math.pi
                    r = random.uniform(0, min(w, h) * 0.35)
                    sx = cx + math.cos(ang) * r
                    sy = cy + math.sin(ang) * r
                    spd = random.uniform(self._spark_speed * 0.6, self._spark_speed * 1.4)
                    dx = math.cos(ang) * spd
                    dy = math.sin(ang) * spd
                    life = random.randint(int(self._spark_life * 0.7), int(self._spark_life * 1.2))
                    sz = random.randint(self._spark_sz_min, self._spark_sz_max)
                    if palette and len(palette) > 0:
                        col = random.choice(palette)
                    else:
                        col = (255, 200, 120)
                    self._sparks.append((sx, sy, dx, dy, 0, life, col))
                if steps > 0:
                    new_sparks: list[tuple[float, float, float, float, int, int, tuple[int, int, int]]] = []
                    for (sx, sy, dx, dy, age, life, col) in self._sparks:
                        sx += dx * steps
                        sy += dy * steps
                        age += steps
                        if age < life and -4 <= sx < w + 4 and -4 <= sy < h + 4:
                            new_sparks.append((sx, sy, dx, dy, age, life, col))
                    self._sparks = new_sparks
        except Exception:
            pass
        self._surf.fill((0, 0, 0, 0))
        cam = DummyCamera(w, h)
        if self._model:
            for p in getattr(self._model, 'pixels_flat', []):
                p.render(self._surf, cam)
        if self._sparks:
            for (sx, sy, dx, dy, age, life, col) in self._sparks:
                alpha = max(0, min(255, int(220 * (1 - age / max(1, life)))))
                sz = max(1, int((self._spark_sz_min + self._spark_sz_max) / 2))
                dot = pygame.Surface((sz, sz), pygame.SRCALPHA)
                dot.fill((*col, alpha))
                ix, iy = int(sx), int(sy)
                if 0 <= ix < w and 0 <= iy < h:
                    self._surf.blit(dot, (ix, iy))
        return self._surf


class ParticlePreviewFirework:
    def __init__(self, color: Tuple[int, int, int] | None = None, speed: float = 12.0) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._model: FireworkLaunchModel | None = None
        self._color = color
        self._speed = speed
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._model = None

    def _ensure_model(self, w: int, h: int) -> None:
        if self._model is None or self._model.finished:
            sx = w / 2.0
            sy = max(4, h - 4)
            tx = w / 2.0 + random.uniform(-w * 0.2, w * 0.2)
            ty = h * 0.25 + random.uniform(-h * 0.1, h * 0.1)
            self._model = FireworkLaunchModel(sx, sy, tx, ty, speed=self._speed)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._ensure_model(w, h)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
        self._surf.fill((0, 0, 0, 0))
        if self._model:
            for pd in self._model.particles:
                if pd.is_dead():
                    continue
                alpha = max(0, min(255, int(255 * (1 - pd.age / max(1, pd.lifespan)))))
                sz = max(1, int(pd.size))
                blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
                col = self._color if self._color is not None else pd.color
                blob.fill((*col, alpha))
                x = int(pd.x)
                y = int(pd.y)
                if 0 <= x < w and 0 <= y < h:
                    self._surf.blit(blob, (x, y))
        return self._surf


class ParticlePreviewLightning:
    def __init__(self, color: Tuple[int, int, int] = (120, 200, 255), segments: int = 10, offset: int = 10, lifetime: int = 8, thickness: int = 2, *, blend_mode: str | None = None, alpha_over_life=None, color_over_life=None) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._model: LightningModel | None = None
        self._color = color
        self._segments = segments
        self._offset = offset
        self._lifetime = lifetime
        self._thickness = thickness
        self._acc_ms = 0
        self._step_ms = 33
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._model = None

    def _ensure_model(self, w: int, h: int) -> None:
        if self._model is None or self._model.is_finished():
            sx = 4
            sy = h // 3
            ex = w - 4
            ey = 2 * h // 3
            self._model = LightningModel((sx, sy), (ex, ey), segments=self._segments, offset=self._offset, lifetime=self._lifetime)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        self._ensure_model(w, h)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
        self._surf.fill((0, 0, 0, 0))
        if self._model:
            frac = max(0.0, min(1.0, self._model.lifetime / max(1, self._model.max_lifetime)))
            if self._alpha_curve is not None:
                a = max(0.0, min(1.0, eval_curve(self._alpha_curve, frac, 1.0)))
                alpha = int(255.0 * a)
            else:
                alpha = int(80 + 175 * frac)
            if self._color_grad is not None:
                dcol = eval_color_gradient(self._color_grad, frac, self._color)
            else:
                dcol = self._color
            col = (*dcol, alpha)
            pts = [(int(x), int(y)) for (x, y) in getattr(self._model, 'points', [])]
            if len(pts) >= 2:
                if self._blend_add:
                    tmp = pygame.Surface(self._size, pygame.SRCALPHA)
                    for dx in (-1, 0, 1):
                        for dy in (-1, 0, 1):
                            pygame.draw.lines(tmp, col, False, [(x + dx, y + dy) for (x, y) in pts], self._thickness)
                    self._surf.blit(tmp, (0, 0), special_flags=pygame.BLEND_ADD)
                else:
                    for dx in (-1, 0, 1):
                        for dy in (-1, 0, 1):
                            pygame.draw.lines(self._surf, col, False, [(x + dx, y + dy) for (x, y) in pts], self._thickness)
        return self._surf
