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
        try:
            smin = int(size_range[0])
            smax = int(size_range[1])
            if smax < smin:
                smin, smax = smax, smin
            self.size = random.randint(max(1, smin), max(1, smax))
        except Exception:
            self.size = random.randint(8, 16)
        self.color = color

    def apply_force(self, force: pygame.math.Vector2):
        self.acceleration += force

    def update(self):
        self.velocity += self.acceleration
        self.pos += self.velocity
        # Decaimiento estándar por tick; mantener compatibilidad con valor anterior (~40 ticks)
        self.lifespan -= 2.5
        self.acceleration *= 0

    def is_dead(self) -> bool:
        return self.lifespan <= 0

    def render(self, screen, camera):
        if self.is_dead():
            return
        screen_pos = camera.apply((self.pos.x, self.pos.y))
        alpha = max(0, min(255, int(self.lifespan * 2.55)))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, alpha))
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
            )
            self.particles.append(p)
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if not p.is_dead()]
