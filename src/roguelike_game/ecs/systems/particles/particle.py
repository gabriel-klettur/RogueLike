# Migrated legacy Particle model into ECS folder for explosion effects
import pygame
import random
import math

class Particle:
    """
    ECS-compatible particle model usable in explosion and other VFX models.
    Backward-compatible defaults: no gravity/drag/curves, alpha fades linearly.
    """
    def __init__(
        self,
        x: float,
        y: float,
        angle: float,
        speed: float,
        color: tuple[int, int, int],
        size: int,
        lifespan: int,
        *,
        gravity: tuple[float, float] | list[float] | None = None,
        drag: float | None = None,
        blend_mode: str | None = None,
        size_over_life: list | tuple | None = None,
        alpha_over_life: list | tuple | None = None,
        color_over_life: list | tuple | None = None,
    ) -> None:
        self.x = float(x)
        self.y = float(y)
        self.dx = math.cos(angle) * float(speed)
        self.dy = math.sin(angle) * float(speed)
        self.color = tuple(color)
        self._color0 = tuple(color)
        self.size = int(size)
        self._size0 = int(size)
        self.lifespan = int(lifespan)
        self.age = 0
        # Advanced runtime options (optional)
        if isinstance(gravity, (list, tuple)) and len(gravity) >= 2:
            self._gx = float(gravity[0])
            self._gy = float(gravity[1])
        else:
            self._gx = 0.0
            self._gy = 0.0
        try:
            dval = float(drag) if isinstance(drag, (int, float)) else 0.0
        except Exception:
            dval = 0.0
        self._drag = max(0.0, min(0.98, dval))
        self._blend_add = isinstance(blend_mode, str) and blend_mode.lower() == "additive"
        self._size_curve = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self._alpha_curve = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self._color_grad = color_over_life if isinstance(color_over_life, (list, tuple)) else None

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

    def update(self):
        # Apply gravity and drag
        self.dx += self._gx
        self.dy += self._gy
        if self._drag > 0:
            self.dx *= (1.0 - self._drag)
            self.dy *= (1.0 - self._drag)
        # Integrate
        self.x += self.dx
        self.y += self.dy
        self.age += 1

    def render(self, screen, camera):
        if self.age >= self.lifespan:
            return
        # normalized age 0..1
        t = max(0.0, min(1.0, self.age / max(1, self.lifespan)))
        # size over life
        if self._size_curve is not None:
            scale = max(0.05, self._eval_curve(self._size_curve, t, 1.0))
            draw_sz = max(1, int(self._size0 * scale))
        else:
            draw_sz = int(self.size)
        # alpha over life
        if self._alpha_curve is not None:
            aval = max(0.0, min(1.0, self._eval_curve(self._alpha_curve, t, 1.0)))
            alpha = max(0, min(255, int(255.0 * aval)))
        else:
            alpha = max(0, int(255 * (1 - self.age / max(1, self.lifespan))))
        # color over life
        col = self._eval_color_gradient(self._color_grad, t, self._color0) if self._color_grad is not None else self.color
        surf = pygame.Surface((draw_sz, draw_sz), pygame.SRCALPHA)
        surf.fill((*col, alpha))
        pos = camera.apply((self.x, self.y))
        if self._blend_add:
            screen.blit(surf, pos, special_flags=pygame.BLEND_ADD)
        else:
            screen.blit(surf, pos)
