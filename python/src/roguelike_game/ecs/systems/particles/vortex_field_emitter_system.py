import random
import time
import math
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.particles.particle_component import ParticleComponent


class VortexFieldEmitterSystem:
    """
    Emite partículas simples para cada ForceFieldComponent para visualizar su centro y modo.
    - pull: color azulado/cian, partículas que se contraen hacia el centro.
    - push: color rojizo/naranja, partículas que salen del centro.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # 20 partículas por segundo por campo (aprox.): periodo 0.05s
        self.period_s = 0.05
        self.per_emit = 3  # 3 partículas por emisión para visibilidad

    @benchmark(lambda self: self.perf_log, 'VortexFieldEmitterSystem.update')
    def update(self, world, camera=None):
        fields = world.components.get('ForceFieldComponent', {})
        if not fields:
            return
        pos_map = world.components.get('Position', {})
        now = time.time()
        for eid, field in list(fields.items()):
            cpos = pos_map.get(eid)
            if cpos is None:
                continue
            last = getattr(field, '_last_emit', None)
            due = 0
            if last is None:
                due = 1
                field._last_emit = now
            else:
                elapsed = now - float(last)
                if elapsed >= self.period_s:
                    due = max(1, int(elapsed / self.period_s))
                    field._last_emit = now
            if due <= 0:
                continue
            # Elegir paleta por modo
            mode = (getattr(field, 'mode', 'pull') or 'pull').lower()
            if mode == 'push':
                base_colors = [(255, 120, 60), (255, 90, 40), (255, 160, 80)]  # naranja/rojo
                dir_sign = 1.0
            else:
                base_colors = [(60, 200, 255), (80, 220, 255), (40, 160, 240)]  # cian
                dir_sign = -1.0
            radius = float(getattr(field, 'radius', 0.0))
            # Emitir varias partículas por ráfaga
            for _e in range(due):
                for _ in range(self.per_emit):
                    # Muestra un ángulo y una distancia corta relativa al radio
                    ang = random.random() * 2.0 * math.pi
                    r = min(16.0, max(6.0, radius * 0.08))
                    ox = math.cos(ang) * r
                    oy = math.sin(ang) * r
                    # Velocidad radial hacia afuera (push) o adentro (pull)
                    spd = random.uniform(1.2, 2.4)
                    dx = math.cos(ang) * spd * dir_sign
                    dy = math.sin(ang) * spd * dir_sign
                    size = random.randint(2, 4)
                    life = random.randint(10, 18)
                    color = random.choice(base_colors)
                    pid = world.create_entity()
                    world.components.setdefault('Position', {})[pid] = Position(cpos.x + ox, cpos.y + oy)
                    world.components.setdefault('ParticleComponent', {})[pid] = ParticleComponent(
                        dx, dy, color, size, life,
                        blend_mode='additive',
                        drag=0.06,
                    )
