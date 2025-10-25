import pygame
import random

class SmokeParticle:
    """
    Partícula individual de humo.
    Incluye posición, física básica, vida y render.
    """
    def __init__(
        self,
        x: float,
        y: float,
        color=(200, 200, 200),
        *,
        base_dir: tuple[float, float] | list[float] = (0.0, -1.0),
        speed: float = 1.0,
        lifespan: float = 100.0,
        size_range: tuple[int, int] | list[int] = (8, 16),
        dispersion: float = 0.3,
        # Advanced, optional runtime params
        gravity: tuple[float, float] | list[float] | None = None,
        drag: float | None = None,
        blend_mode: str | None = None,
        size_over_life: list | tuple | None = None,
        alpha_over_life: list | tuple | None = None,
        color_over_life: list | tuple | None = None,
    ):
        self.pos = pygame.math.Vector2(x, y)
        # Dirección base normalizada (por defecto hacia arriba)
        bx, by = (base_dir[0], base_dir[1]) if isinstance(base_dir, (list, tuple)) and len(base_dir) >= 2 else (0.0, -1.0)
        base = pygame.math.Vector2(float(bx), float(by))
        if base.length_squared() == 0:
            base = pygame.math.Vector2(0.0, -1.0)
        base = base.normalize()
        # Variación aleatoria gaussiana controlada por 'dispersion'
        jitter = pygame.math.Vector2(random.gauss(0, dispersion), random.gauss(0, dispersion))
        self.velocity = base * float(speed) + jitter
        self.acceleration = pygame.math.Vector2(0, 0)
        try:
            self.lifespan = float(lifespan)
        except Exception:
            self.lifespan = 100.0
        # Keep initial values for normalized-age evaluations
        self._life0 = float(self.lifespan)
        try:
            smin = int(size_range[0])
            smax = int(size_range[1])
            if smax < smin:
                smin, smax = smax, smin
            self.size = random.randint(max(1, smin), max(1, smax))
        except Exception:
            self.size = random.randint(8, 16)
        self.color = color
        self._size0 = int(self.size)
        self._color0 = tuple(color)
        # Advanced runtime options
        if isinstance(gravity, (list, tuple)) and len(gravity) >= 2:
            self._g = pygame.math.Vector2(float(gravity[0]), float(gravity[1]))
        else:
            self._g = pygame.math.Vector2(0.0, 0.0)
        try:
            dval = float(drag) if isinstance(drag, (int, float)) else 0.0
        except Exception:
            dval = 0.0
        self._drag = max(0.0, min(0.98, dval))
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None

    def apply_force(self, force: pygame.math.Vector2):
        self.acceleration += force

    def update(self):
        # Apply constant gravity and drag, then integrate
        self.acceleration += self._g
        self.velocity += self.acceleration
        if self._drag > 0:
            self.velocity *= (1.0 - self._drag)
        self.pos += self.velocity
        # Decaimiento estándar por tick; mantener compatibilidad con valor anterior (~40 ticks)
        self.lifespan -= 2.5
        self.acceleration *= 0

    def is_dead(self) -> bool:
        return self.lifespan <= 0

    def _eval_curve(self, curve, t: float, default: float) -> float:
        if not isinstance(curve, (list, tuple)) or len(curve) == 0:
            return float(default)
        pts = []
        for pt in curve:
            try:
                pts.append((float(pt[0]), float(pt[1])))
            except Exception:
                continue
        if not pts:
            return float(default)
        pts.sort(key=lambda x: x[0])
        if t <= pts[0][0]:
            return pts[0][1]
        if t >= pts[-1][0]:
            return pts[-1][1]
        for i in range(1, len(pts)):
            t0, v0 = pts[i-1]
            t1, v1 = pts[i]
            if t0 <= t <= t1 and t1 > t0:
                k = (t - t0) / (t1 - t0)
                return v0 * (1 - k) + v1 * k
        return float(default)

    def _eval_color_gradient(self, grad, t: float, base):
        if not isinstance(grad, (list, tuple)) or len(grad) == 0:
            return base
        pts = []
        for pt in grad:
            try:
                col = pt[1]
                if isinstance(col, (list, tuple)) and len(col) >= 3:
                    pts.append((float(pt[0]), (int(col[0]), int(col[1]), int(col[2]))))
            except Exception:
                continue
        if not pts:
            return base
        pts.sort(key=lambda x: x[0])
        if t <= pts[0][0]:
            return pts[0][1]
        if t >= pts[-1][0]:
            return pts[-1][1]
        for i in range(1, len(pts)):
            t0, c0 = pts[i-1]
            t1, c1 = pts[i]
            if t0 <= t <= t1 and t1 > t0:
                k = (t - t0) / (t1 - t0)
                r = int(c0[0] * (1 - k) + c1[0] * k)
                g = int(c0[1] * (1 - k) + c1[1] * k)
                b = int(c0[2] * (1 - k) + c1[2] * k)
                return (r, g, b)
        return base

    def render(self, screen, camera):
        if self.is_dead():
            return
        screen_pos = camera.apply((self.pos.x, self.pos.y))
        # Normalized age t in [0,1]
        t = 1.0 - max(0.0, min(1.0, self.lifespan / max(1e-3, self._life0)))
        # Size over life
        if self._size_curve is not None:
            scale = max(0.05, self._eval_curve(self._size_curve, t, 1.0))
            draw_sz = max(1, int(self._size0 * scale))
        else:
            draw_sz = int(self.size)
        # Alpha over life (override default if provided)
        if self._alpha_curve is not None:
            aval = max(0.0, min(1.0, self._eval_curve(self._alpha_curve, t, 1.0)))
            alpha = max(0, min(255, int(255.0 * aval)))
        else:
            alpha = max(0, min(255, int(self.lifespan * 2.55)))
        # Color over life gradient
        col = self._eval_color_gradient(self._color_grad, t, self._color0) if self._color_grad is not None else self.color
        surf = pygame.Surface((draw_sz, draw_sz), pygame.SRCALPHA)
        surf.fill((*col, alpha))
        if self._blend_add:
            screen.blit(surf, screen_pos, special_flags=pygame.BLEND_ADD)
        else:
            screen.blit(surf, screen_pos)

class SmokeEmitterModel:
    """
    Modelo para emisor de humo: origen, color de partículas y tasa de emisión.
    """
    def __init__(
        self,
        x: float,
        y: float,
        color=(200, 200, 200),
        emit_rate: int = 2,
        *,
        speed: float = 1.0,
        lifespan: float = 100.0,
        size_range: tuple[int, int] | list[int] = (8, 16),
        dispersion: float = 0.3,
        colors_palette: list[tuple[int, int, int]] | None = None,
        direction: tuple[float, float] | list[float] = (0.0, -1.0),
        # Advanced, optional runtime params for particles
        gravity: tuple[float, float] | list[float] | None = None,
        drag: float | None = None,
        blend_mode: str | None = None,
        size_over_life: list | tuple | None = None,
        alpha_over_life: list | tuple | None = None,
        color_over_life: list | tuple | None = None,
    ):
        self.origin = pygame.math.Vector2(x, y)
        self.color = color
        self.emit_rate = int(max(1, emit_rate))
        self.p_speed = float(speed)
        self.p_lifespan = float(lifespan)
        # Normalizar size_range
        try:
            smin = int(size_range[0])
            smax = int(size_range[1])
            if smax < smin:
                smin, smax = smax, smin
            self.p_size_range = (max(1, smin), max(1, smax))
        except Exception:
            self.p_size_range = (8, 16)
        self.p_dispersion = float(max(0.0, dispersion))
        self.palette = list(colors_palette) if colors_palette else None
        self.base_dir = direction if isinstance(direction, (list, tuple)) and len(direction) >= 2 else (0.0, -1.0)
        self.particles: list[SmokeParticle] = []
        # Advanced runtime options stored at emitter-level and passed to new particles
        self._grav = tuple(gravity) if isinstance(gravity, (list, tuple)) and len(gravity) >= 2 else None
        try:
            self._drag = float(drag) if isinstance(drag, (int, float)) else None
        except Exception:
            self._drag = None
        self._blend_mode = blend_mode if isinstance(blend_mode, str) else None
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None

    def is_empty(self) -> bool:
        return not self.particles

    def update(self):
        for _ in range(self.emit_rate):
            # Seleccionar color (paleta o color base)
            col = self.color
            if self.palette:
                try:
                    col = random.choice(self.palette)
                except Exception:
                    col = self.color
            p = SmokeParticle(
                self.origin.x,
                self.origin.y,
                col,
                base_dir=self.base_dir,
                speed=self.p_speed,
                lifespan=self.p_lifespan,
                size_range=self.p_size_range,
                dispersion=self.p_dispersion,
                gravity=self._grav,
                drag=self._drag,
                blend_mode=self._blend_mode,
                size_over_life=self._size_curve,
                alpha_over_life=self._alpha_curve,
                color_over_life=self._color_grad,
            )
            self.particles.append(p)
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if not p.is_dead()]
