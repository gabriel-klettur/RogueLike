import pygame
import random
import math
from typing import Tuple
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import (
    SmokeEmitterModel,
    SmokeParticle,
)
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.model import (
    FireworkLaunchModel,
)
from roguelike_game.ecs.components.abilities.lightning_model import LightningModel

from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.model import (
    ArcaneFlameModel,
)
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.palette import (
    CELL_SIZE,
)


class _DummyCamera:
    def __init__(self, w: int | None = None, h: int | None = None):
        self.w = w
        self.h = h

    def apply(self, pos: Tuple[float, float]) -> Tuple[int, int]:
        # Identity transform for preview surfaces
        return int(pos[0]), int(pos[1])

    def is_in_view(self, x: float, y: float, size: Tuple[int, int]) -> bool:
        # Basic bounds check when dimensions are known; otherwise allow
        if self.w is None or self.h is None:
            return True
        sw, sh = size
        return (-sw <= x < self.w and -sh <= y < self.h)


class ParticlePreviewSmoke:
    """Simple smoke particle preview that renders into a provided Surface size.

    It simulates a small SmokeEmitterModel in local coords and draws to an
    offscreen Surface with transparent background.
    """

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
        self._camera = _DummyCamera()
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._warm_started = False
        self._warm_steps = max(0, int(warm_start_steps))
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._warm_started = False

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        # Place emitter near bottom-center so smoke rises inside the cell
        self.model.origin.update(w / 2.0, max(4, h - 6))

        # Warm start once to fill some particles on first frame
        if not self._warm_started:
            for _ in range(self._warm_steps):
                self.model.update()
            self._warm_started = True
        # Step simulation using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self.model.update()
            self._acc_ms -= self._step_ms

        # Clear and draw
        self._surf.fill((0, 0, 0, 0))
        # Inline render to avoid importing the full View class
        for p in self.model.particles:
            if p.is_dead():
                continue
            # Adapted from SmokeParticle.render but without screen camera mapping
            alpha = max(0, min(255, int(p.lifespan * 2.55)))
            sz = max(1, int(p.size))
            blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
            blob.fill((*p.color, alpha))
            x, y = self._camera.apply((p.pos.x, p.pos.y))
            # Clamp inside surface bounds to avoid blits outside
            if 0 <= x < w and 0 <= y < h:
                self._surf.blit(blob, (x, y))
        return self._surf


class ParticlePreviewSmokeBurst:
    """Single-burst smoke preview distinct from continuous emitter.

    Spawns a one-time burst of `SmokeParticle`s with an initial direction and
    lets them dissipate. When all are gone, respawns for looping preview.
    """

    def __init__(
        self,
        color=(200, 200, 200),
        count: int = 12,
        direction: tuple[float, float] | None = None,
        warm_start_steps: int = 6,
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
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

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
            # Apply initial directional force with some spread
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
        # Warm start a bit so particles are not all at t=0
        if not self._warm_started:
            for _ in range(self._warm_steps):
                for p in self._parts:
                    p.update()
                self._parts = [p for p in self._parts if not p.is_dead()]
                if not self._parts:
                    self._spawn_burst(w, h)
            self._warm_started = True
        # Step simulation using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            for p in self._parts:
                p.update()
            self._parts = [p for p in self._parts if not p.is_dead()]
            if not self._parts:
                self._spawn_burst(w, h)
            self._acc_ms -= self._step_ms

        # Draw
        self._surf.fill((0, 0, 0, 0))
        for p in self._parts:
            if p.is_dead():
                continue
            alpha = max(0, min(255, int(p.lifespan * 2.55)))
            sz = max(1, int(p.size))
            blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
            blob.fill((*p.color, alpha))
            x, y = int(p.pos.x), int(p.pos.y)
            if 0 <= x < w and 0 <= y < h:
                self._surf.blit(blob, (x, y))
        return self._surf

# -------- Generic lightweight previews for more particle-based effects --------

class ParticlePreviewHealingAura:
    """Healing aura-like preview: rising particles inside an ellipse.

    Parameters roughly mirror spells.json vfx.particles for healing_aura:
    - palette/colors, emit_rate, speed, lifespan, size_range, radius
    """

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
        # x,y,vx,vy,age,life,size,color
        self._warm_started = False
        self._warm_steps = max(0, int(warm_start_steps))
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._parts.clear()
            self._warm_started = False

    def _ellipse_dims(self, w: int, h: int) -> tuple[int, int, int, int]:
        # Center and half-dimensions for spawn ellipse
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
            # Rejection sample inside ellipse
            for _tries in range(8):
                dx = random.uniform(-hw, hw)
                dy = random.uniform(-hh, hh)
                if (dx / hw) ** 2 + (dy / hh) ** 2 <= 1:
                    break
            x = cx + dx
            y = max(top, min(bottom, cy + dy))
            spd = abs(self._speed)
            vy = -spd if spd > 0 else -1.0
            vx = random.uniform(-0.3, 0.3) * (self._speed if self._speed != 0 else 1.0)
            # Frames until reaching the top bound
            frames_to_top = int((y - top) / max(0.001, abs(vy)))
            life = min(self._lifespan, max(8, frames_to_top))
            size = random.randint(self._smin, self._smax)
            col = self._color
            if self._palette:
                col = self._palette[random.randrange(len(self._palette))]
            self._parts.append((x, y, vx, vy, 0, life, size, col))

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        # Warm start to prefill some particles
        if not self._warm_started:
            for _ in range(self._warm_steps):
                self._spawn(w, h)
                # Advance a frame for existing particles
                self._parts = [
                    (x + vx, y + vy, vx, vy, age + 1, life, sz, col)
                    for (x, y, vx, vy, age, life, sz, col) in self._parts
                    if age + 1 < life
                ]
            self._warm_started = True
        # Step simulation using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self._spawn(w, h)
            self._parts = [
                (x + vx, y + vy, vx, vy, age + 1, life, sz, col)
                for (x, y, vx, vy, age, life, sz, col) in self._parts
                if age + 1 < life
            ]
            self._acc_ms -= self._step_ms

        # Draw
        self._surf.fill((0, 0, 0, 0))
        for (x, y, vx, vy, age, life, sz, col) in self._parts:
            alpha = max(0, min(255, int(255 * (1 - age / max(1, life)))))
            blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
            blob.fill((*col, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                self._surf.blit(blob, (ix, iy))
        return self._surf

class ParticlePreviewAura:
    """Pulsing circular aura made of small fading dots around center."""

    def __init__(self, color: Tuple[int, int, int] = (120, 255, 180), radius: int | None = None, speed: float = 1.0, count: int = 24, palette: list[Tuple[int, int, int]] | None = None) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._palette = palette
        self._radius = radius
        self._theta = 0.0
        self._speed = speed
        self._count = count

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
        # If an explicit radius was provided from spell data, clamp it to fit the preview cell.
        # This avoids invisible rings when the gameplay radius is much larger than the 64px cell.
        if isinstance(self._radius, int):
            max_r = max(8, min(w, h) // 2 - 4)
            radius = max(8, min(max_r, self._radius))
        else:
            radius = max(8, min(w, h) // 3)
        cx, cy = w // 2, h // 2
        for i in range(self._count):
            t = self._theta + (i / self._count) * (2 * 3.14159)
            x = int(cx + radius * math.cos(t))
            y = int(cy + radius * math.sin(t))
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
    """Trailing streaks behind a moving dot to suggest dash movement."""

    def __init__(self, color: Tuple[int, int, int] = (180, 220, 255), speed_px: float = 60.0) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._pos = 0.0
        self._trail: list[tuple[float, float, int]] = []  # x, y, age
        self._speed = speed_px
        # Fixed-timestep accumulator for stable trail timing (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

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
        # Step simulation using fixed-timestep accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            # advance head position by fixed time slice
            self._pos += self._speed * (self._step_ms / 1000.0)
            # sample trail at current head
            w_step, h_step = self._size
            px_step = int(self._pos % max(1, w_step - 6)) + 3
            py_step = h_step // 2
            self._trail.append((px_step, py_step, 0))
            # age trail in fixed steps
            self._trail = [(x, y, age + 1) for (x, y, age) in self._trail if age + 1 < 24]
            self._acc_ms -= self._step_ms
        # Draw current state
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
                self._surf.blit(surf, (x0, y))
        # head
        head = pygame.Surface((3, 3), pygame.SRCALPHA)
        head.fill((*self._color, 255))
        self._surf.blit(head, (px, py))
        return self._surf


class ParticlePreviewSlash:
    """Curved arc particles to suggest a slash swing."""

    def __init__(self, color: Tuple[int, int, int] = (255, 230, 150), speed: float = 2.5) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._angle = 0.0
        self._speed = speed

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
        # draw multiple points along a 90-degree arc
        for i in range(14):
            t = self._angle + (i / 14) * (math.pi / 2)
            x = int(cx + radius * math.cos(t))
            y = int(cy + radius * math.sin(t))
            alpha = 220 - i * 12
            dot = pygame.Surface((3, 3), pygame.SRCALPHA)
            dot.fill((*self._color, max(0, alpha)))
            if 0 <= x < w and 0 <= y < h:
                self._surf.blit(dot, (x, y))
        return self._surf


class ParticlePreviewLaser:
    """Simple horizontal laser bar with random spark particles."""

    def __init__(self, color: Tuple[int, int, int] = (120, 200, 255)) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._sparks: list[tuple[int, int, int]] = []  # x, y, age
        # Fixed-timestep accumulator for stable spark timing (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._sparks.clear()

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        # Update using fixed-timestep accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            w_step, h_step = self._size
            y_step = h_step // 2
            if random.random() < 0.7:
                self._sparks.append((random.randint(4, max(4, w_step - 5)), y_step + random.randint(-4, 4), 0))
            self._sparks = [(x, sy, age + 1) for (x, sy, age) in self._sparks if age + 1 < 20]
            self._acc_ms -= self._step_ms
        # Draw after updates
        w, h = self._size
        self._surf.fill((0, 0, 0, 0))
        y = h // 2
        # laser bar
        pygame.draw.rect(self._surf, (*self._color, 200), pygame.Rect(2, y-1, max(1, w-4), 2))
        # sparks
        for (x, sy, age) in self._sparks:
            alpha = max(0, 200 - age * 10)
            dot = pygame.Surface((2, 2), pygame.SRCALPHA)
            dot.fill((*self._color, alpha))
            self._surf.blit(dot, (x, sy))
        return self._surf


class ParticlePreviewExplosion:
    """Small center explosion loop with optional color palette.

    Parameters:
    - color: base color if palette not provided
    - palette: optional list of colors to pick per particle
    - count: number of particles to spawn per burst
    - speed_range: (min,max) speed for radial particles
    """

    def __init__(self, color: Tuple[int, int, int] = (255, 180, 80), palette: list[Tuple[int, int, int]] | None = None, count: int = 24, speed_range: tuple[float, float] = (0.8, 2.5)) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        self._palette = palette
        self._count = max(6, int(count))
        lo, hi = speed_range
        self._spd_lo = float(min(lo, hi))
        self._spd_hi = float(max(lo, hi))
        self._parts: list[tuple[float, float, float, float, int]] = []  # x,y,dx,dy,age
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

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
        # Update using accumulator (~30 Hz)
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
                # respawn loop to keep animation alive
                self._spawn(w, h)
            self._acc_ms -= self._step_ms
        # Draw after updates
        for (x, y, dx, dy, age) in self._parts:
            alpha = max(0, 220 - age * 7)
            dot = pygame.Surface((3, 3), pygame.SRCALPHA)
            if self._palette and len(self._palette) > 0:
                col = random.choice(self._palette)
            else:
                col = self._color
            dot.fill((*col, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                self._surf.blit(dot, (ix, iy))
        return self._surf


class ParticlePreviewArcaneFlame:
    """Preview using the runtime ArcaneFlameModel for high fidelity, plus sparks.

    Fits a CELL_SIZE-aligned grid inside the given cell and renders its pixels.
    Adds lightweight overlay spark particles to better match in-game visuals.
    """

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
        # Sparks overlay params
        self._spark_rate = max(0, int(spark_rate))
        self._spark_speed = float(spark_speed)
        self._spark_sz_min = int(min(spark_size_range))
        self._spark_sz_max = int(max(spark_size_range))
        self._spark_life = max(6, int(spark_lifespan))
        self._sparks: list[tuple[float, float, float, float, int, int, tuple[int, int, int]]] = []
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._model = None

    def _ensure_model(self, w: int, h: int) -> None:
        # For the editor preview we keep the flame continuous; only create once
        if self._model is None:
            # Choose a grid that fits inside the cell and aligns to CELL_SIZE
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
        # Step the model using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        steps = 0
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
            steps += 1
        # Inject base heat on the bottom rows to keep the flame continuous in preview
        try:
            if self._model is not None:
                rows = getattr(self._model, 'rows', 0)
                cols = getattr(self._model, 'columns', 0)
                pixels = getattr(self._model, 'pixels', None)
                if pixels and rows > 0 and cols > 0:
                    br = rows - 1
                    # Strong heat at the bottom row
                    for c in range(cols):
                        p = pixels[br][c]
                        if p and random.random() < 0.85:
                            p.idx = 0
                    # Softer heat just above bottom
                    br2 = rows - 2
                    if br2 >= 0:
                        for c in range(cols):
                            p = pixels[br2][c]
                            if p and random.random() < 0.35:
                                p.idx = min(p.idx, 1)
        except Exception:
            pass
        # Update overlay sparks similar to runtime emitter
        try:
            if self._spark_rate > 0:
                # Spawn a modest number of sparks per simulated step, scaled down
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
                # Step sparks according to number of simulated steps
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
            # Fail-safe: keep preview running even if spark update fails
            pass
        # Draw all pixels
        self._surf.fill((0, 0, 0, 0))
        cam = _DummyCamera(w, h)
        if self._model:
            for p in getattr(self._model, 'pixels_flat', []):
                p.render(self._surf, cam)
        # Draw sparks on top
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
    """Firework rocket + explosion preview using FireworkLaunchModel."""

    def __init__(self, color: Tuple[int, int, int] | None = None, speed: float = 12.0) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._model: FireworkLaunchModel | None = None
        self._color = color  # If None, use model defaults/randoms
        self._speed = speed
        # Fixed-timestep accumulator for stable speed (~30 Hz)
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
        # Step model using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
        # Clear and draw
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
    """Simple lightning bolt preview using LightningModel with looping."""

    def __init__(self, color: Tuple[int, int, int] = (120, 200, 255), segments: int = 10, offset: int = 10, lifetime: int = 8, thickness: int = 2) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._model: LightningModel | None = None
        self._color = color
        self._segments = segments
        self._offset = offset
        self._lifetime = lifetime
        self._thickness = thickness
        # Fixed-timestep accumulator for stable speed (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

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
        # Step model using accumulator (~30 Hz)
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            assert self._model is not None
            self._model.update()
            self._acc_ms -= self._step_ms
        # Clear and draw polyline with fade by lifetime fraction
        self._surf.fill((0, 0, 0, 0))
        if self._model:
            frac = max(0.0, min(1.0, self._model.lifetime / max(1, self._model.max_lifetime)))
            alpha = int(80 + 175 * frac)
            col = (*self._color, alpha)
            # Draw thick line by multiple offsets for a glow-ish look
            pts = [(int(x), int(y)) for (x, y) in getattr(self._model, 'points', [])]
            if len(pts) >= 2:
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        pygame.draw.lines(self._surf, col, False, [(x+dx, y+dy) for (x, y) in pts], self._thickness)
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
        self._phase = 'out'  # toggles between 'out' and 'in' for looping

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        self._surf.fill((0, 0, 0, 0))
        w, h = self._size
        # advance timer
        self._elapsed_ms += max(0, dt_ms)
        if self._elapsed_ms >= self._cycle_ms:
            self._elapsed_ms -= self._cycle_ms
            self._phase = 'in' if self._phase == 'out' else 'out'
        # progress in [0,1]
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
            # radius might be too large for tiny cells; clamp silently
            r = max(1, min(radius, max(1, min(w, h) // 2 - 1)))
            pygame.draw.circle(self._surf, col, (cx, cy), r, width=3)
        return self._surf


class ParticlePreviewWaterFountain:
    """Water fountain preview: thin falling streams with gravity and splashes.

    Parameters:
    - color: RGB base color for droplets.
    - spouts: list of normalized X in [0..1] where jets originate (top area).
    - emit_rate: droplets spawned per step per spout (~30 Hz steps).
    - speed: initial vertical speed (downwards positive); also slight x spread.
    - gravity: per-step acceleration added to vy.
    - droplet_size: base droplet pixel size.
    - splash_count: number of small splash particles spawned on impact.
    """

    def __init__(
        self,
        color: Tuple[int, int, int] = (100, 180, 255),
        spouts: list[float] | tuple[float, ...] = (0.34, 0.5, 0.66),
        emit_rate: int = 5,
        speed: float = 2.0,
        gravity: float = 0.25,
        droplet_size: int = 2,
        splash_count: int = 2,
    ) -> None:
        self._surf: pygame.Surface | None = None
        self._size: Tuple[int, int] | None = None
        self._color = color
        # sanitize spouts
        self._spouts = [float(max(0.05, min(0.95, s))) for s in list(spouts)] if spouts else [0.5]
        self._emit = max(1, int(emit_rate))
        self._speed = float(speed)
        self._g = float(gravity)
        self._sz = max(1, int(droplet_size))
        self._splash = max(0, int(splash_count))
        # particles: lists of tuples
        # droplets: x, y, vx, vy, size, age, life
        self._drops: list[tuple[float, float, float, float, int, int, int]] = []
        # splashes: x, y, vx, vy, size, age, life
        self._spl: list[tuple[float, float, float, float, int, int, int]] = []
        # Fixed-timestep accumulator (~30 Hz)
        self._acc_ms = 0
        self._step_ms = 33

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._drops.clear()
            self._spl.clear()

    def _spawn_droplets(self, w: int, h: int) -> None:
        top_y = max(2, int(h * 0.18))
        for s in self._spouts:
            x = int(2 + s * max(1, w - 4))
            for _ in range(self._emit):
                # small x-offset to create thin stream and flicker
                vx = random.uniform(-0.2, 0.2) * max(0.5, self._speed * 0.35)
                vy = abs(self._speed) + random.uniform(-0.2, 0.2)
                size = max(1, int(self._sz + random.choice((-1, 0, 0, 1))))
                life = 120  # upper bound; most will end on impact earlier
                self._drops.append((float(x), float(top_y), float(vx), float(vy), size, 0, life))

    def _update_step(self, w: int, h: int) -> None:
        # spawn
        self._spawn_droplets(w, h)
        ground = h - 3
        new_drops: list[tuple[float, float, float, float, int, int, int]] = []
        # update droplets
        for (x, y, vx, vy, sz, age, life) in self._drops:
            vy += self._g
            x += vx
            y += vy
            age += 1
            if y >= ground:
                # splash on impact
                if self._splash > 0:
                    for _ in range(self._splash):
                        ang = random.uniform(-0.9, -2.2)  # up-left to up-right
                        spd = random.uniform(0.8, 1.6) * (0.6 + 0.4 * (sz / max(1, self._sz)))
                        svx = math.cos(ang) * spd
                        svy = math.sin(ang) * spd
                        ssz = max(1, sz - 1)
                        slife = random.randint(10, 24)
                        self._spl.append((x, float(ground), svx, svy, ssz, 0, slife))
                continue  # drop is consumed
            if age < life and -4 <= x < w + 4 and -4 <= y < h + 6:
                new_drops.append((x, y, vx, vy, sz, age, life))
        self._drops = new_drops
        # update splashes (with gravity and fade)
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
        # advance in fixed steps
        self._acc_ms += max(0, dt_ms)
        while self._acc_ms >= self._step_ms:
            self._update_step(w, h)
            self._acc_ms -= self._step_ms
        # draw
        self._surf.fill((0, 0, 0, 0))
        base = self._color
        # stream hint: faint vertical guides (optional aesthetic)
        try:
            for s in self._spouts:
                x = int(2 + s * max(1, w - 4))
                pygame.draw.line(self._surf, (*base, 40), (x, int(h * 0.18)), (x, h - 3), 1)
        except Exception:
            pass
        # droplets (slightly translucent)
        for (x, y, vx, vy, sz, age, life) in self._drops:
            alpha = max(80, min(255, 220 - age))
            blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
            blob.fill((*base, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                self._surf.blit(blob, (ix, iy))
        # splashes brighter but short-lived
        for (x, y, vx, vy, sz, age, life) in self._spl:
            alpha = max(0, min(255, int(255 * (1 - age / max(1, life)))))
            blob = pygame.Surface((sz, sz), pygame.SRCALPHA)
            col = (min(255, base[0] + 20), min(255, base[1] + 20), min(255, base[2] + 20))
            blob.fill((*col, alpha))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                self._surf.blit(blob, (ix, iy))
        return self._surf


class ParticlePreviewFallingLeaf:
    """Single falling leaf at a sparse interval with gentle sway.

    Spawns at most one leaf every `interval_ms`. To avoid an entirely empty
    picker cell, the first spawn uses a randomized phase offset so the preview
    often shows a leaf shortly after appearing.
    """

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
        # State
        self._timer_ms = random.randint(0, self._interval - 1)  # randomize first spawn
        self._acc_ms = 0
        self._step_ms = 33
        # current leaf (None or tuple fields)
        self._leaf: tuple[float, float, float, float, float, int] | None = None
        # x, y, vx, vy, sway_phase, age_ms

    def _ensure_surface(self, size: Tuple[int, int]) -> None:
        if self._size != size or self._surf is None:
            self._size = size
            self._surf = pygame.Surface(size, pygame.SRCALPHA)
            self._leaf = None
            # keep timer, do not reset, so cadence persists while browsing

    def _spawn_leaf(self, w: int, h: int) -> None:
        # Spawn near top canopy
        x = random.uniform(w * 0.25, w * 0.75)
        y = random.uniform(h * 0.05, h * 0.25)
        vx = 0.0
        vy = max(0.1, self._base_vy)
        sway_phase = random.random() * 6.28318
        self._leaf = (x, y, vx, vy, sway_phase, 0)

    def _step(self, w: int, h: int, steps: int) -> None:
        # Handle spawn timer (in real milliseconds)
        self._timer_ms += steps * self._step_ms
        if self._leaf is None and self._timer_ms >= self._interval:
            self._timer_ms %= self._interval
            self._spawn_leaf(w, h)
        if self._leaf is None:
            return
        x, y, vx, vy, phase, age = self._leaf
        for _ in range(steps):
            # update physics in small steps
            # sway: horizontal drift by sinusoid
            phase += self._sway_speed
            vx = self._sway_amp * math.sin(phase)
            vy += self._g
            x += vx
            y += vy
            age += self._step_ms
            # stop if out or lifetime exceeded
            if y >= h - 2 or age >= self._life_ms:
                self._leaf = None
                return
        self._leaf = (x, y, vx, vy, phase, age)

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        # advance in fixed steps
        self._acc_ms += max(0, dt_ms)
        steps = 0
        while self._acc_ms >= self._step_ms:
            self._step(w, h, 1)
            self._acc_ms -= self._step_ms
            steps += 1
        # draw
        self._surf.fill((0, 0, 0, 0))
        if self._leaf is not None:
            x, y, vx, vy, phase, age = self._leaf
            # subtle alpha fade with age
            a = max(80, min(255, 255 - int(255 * (age / max(1, self._life_ms)))))
            leaf = pygame.Surface((self._leaf_w, self._leaf_h), pygame.SRCALPHA)
            leaf.fill((*self._color, a))
            ix, iy = int(x), int(y)
            if 0 <= ix < w and 0 <= iy < h:
                self._surf.blit(leaf, (ix, iy))
        return self._surf


class ParticlePreviewWaterFlow:
    """Tiled flowing water preview using scrolling highlight stripes.

    - Synchronized via global time so adjacent instances do not show seams.
    - Supports horizontal or vertical flow using a direction vector.
    - Designed to be overlaid on top of a dark water tile (uses alpha).
    """

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
        self._speed = float(speed)  # pixels per second-like; scaled from ms
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
        # Offset advances with time; use global ticks to keep tiles in sync
        px_per_ms = self._speed  # interpret as px/ms
        offset = int((t_ms * px_per_ms) % self._gap)
        # vertical highlight stripes
        for x in range(-offset, w + self._gap, self._gap):
            # slight ripple in alpha along y
            col = (*self._hl, self._aw)
            pygame.draw.rect(self._surf, col, pygame.Rect(x, 0, 2, h))

    def _draw_vertical(self, w: int, h: int, t_ms: int) -> None:
        assert self._surf is not None
        self._surf.fill((*self._base, self._ab))
        px_per_ms = self._speed
        offset = int((t_ms * px_per_ms) % self._gap)
        # horizontal highlight stripes
        for y in range(-offset, h + self._gap, self._gap):
            col = (*self._hl, self._aw)
            pygame.draw.rect(self._surf, col, pygame.Rect(0, y, w, 2))

    def render(self, size: Tuple[int, int], dt_ms: int) -> pygame.Surface:
        self._ensure_surface(size)
        assert self._surf is not None and self._size is not None
        w, h = self._size
        # global synchronized time
        t_ms = pygame.time.get_ticks()
        # Choose axis by dominant component
        if abs(self._dir.x) >= abs(self._dir.y):
            self._draw_horizontal(w, h, t_ms)
        else:
            self._draw_vertical(w, h, t_ms)
        # Simple ripple overlay: sine-based alpha modulation
        if self._ripple > 0:
            try:
                ripple = pygame.Surface((w, h), pygame.SRCALPHA)
                # Modulate rows/cols depending on direction
                if abs(self._dir.x) >= abs(self._dir.y):
                    # horizontal flow: ripple across y
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
