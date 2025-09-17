import random
import math
from roguelike_game.ecs.systems.particles.particle import Particle

class FireExplosionModel:
    def __init__(self, x, y, particle_count=100, scale=1.0, colors=None):
        self.x = x
        self.y = y
        # Allow scalable explosion size via 'scale' factor (>=0.1)
        try:
            sc = float(scale)
        except Exception:
            sc = 1.0
        sc = max(0.1, sc)
        count = max(1, int(particle_count * sc))
        size_min = max(1, int(round(6 * sc)))
        size_max = max(size_min, int(round(10 * sc)))
        # Resolve colors palette (fallback to warm fire palette)
        default_palette = [(255, 80, 0), (255, 180, 0), (255, 255, 0)]
        palette = default_palette
        try:
            if isinstance(colors, (list, tuple)) and len(colors) > 0:
                tmp = []
                for c in colors:
                    if isinstance(c, (list, tuple)) and len(c) >= 3:
                        tmp.append((int(c[0]), int(c[1]), int(c[2])))
                if tmp:
                    palette = tmp
        except Exception:
            palette = default_palette
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(4, 8),
                color=random.choice(palette),
                size=random.randint(size_min, size_max),
                lifespan=random.randint(20, 35)
            )
            for _ in range(count)
        ]
        self.finished = False

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

class TimedEffectModel:
    """Minimal TTL-driven model for preset-based effects.

    Attach this model to an entity together with ParticlePresetComponent so
    ExplosionSystem can remove it automatically when time is up. The render
    is performed by ParticlePresetRenderSystem; here we provide a no-op render
    to satisfy ExplosionRenderSystem's generic call.
    """
    def __init__(self, ttl_ticks: int = 30):
        try:
            self.ttl = max(1, int(ttl_ticks))
        except Exception:
            self.ttl = 30
        self._age = 0
        self.finished = False

    def update(self):
        if not self.finished:
            self._age += 1
            if self._age >= self.ttl:
                self.finished = True

    def render(self, screen, camera):
        # Rendered by ParticlePresetRenderSystem; nothing to draw here.
        return None
class ElectricExplosionModel:
    def __init__(self, x, y, particle_count=35):
        self.x = x
        self.y = y
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(3, 6),
                color=random.choice([(0, 255, 255), (150, 255, 255), (255, 255, 255)]),
                size=random.randint(1, 4),
                lifespan=random.randint(10, 20)
            )
            for _ in range(particle_count)
        ]
        self.finished = False

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

class DarkExplosionModel:
    def __init__(self, x, y, particle_count=25):
        self.x = x
        self.y = y
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(1, 3),
                color=random.choice([(40, 0, 40), (60, 0, 60), (20, 20, 20)]),
                size=random.randint(5, 10),
                lifespan=random.randint(40, 60)
            )
            for _ in range(particle_count)
        ]
        self.finished = False

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

class MagicExplosionModel:
    def __init__(self, x, y, particle_count=30):
        self.x = x
        self.y = y
        self.particles = []
        self.finished = False
        for _ in range(particle_count):
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(2, 6)
            size = random.randint(2, 6)
            lifespan = random.randint(20, 40)
            color = random.choice([
                (100, 100, 255),
                (180, 80, 255),
                (50, 255, 255),
                (255, 255, 255)
            ])
            self.particles.append(Particle(x, y, angle, speed, color, size, lifespan))

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
