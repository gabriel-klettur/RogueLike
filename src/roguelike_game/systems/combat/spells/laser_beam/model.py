# Path: src/roguelike_game/systems/combat/spells/laser_beam/model.py
import math
import random
from src.roguelike_game.systems.particles.particle import Particle
from src.roguelike_game.systems.combat.explosions.electric import ElectricExplosion

class LaserBeamModel:
    """
    Modelo puro para el rayo láser: datos de posición, partículas y explosión.
    """
    def __init__(self, x1, y1, x2, y2, particle_count=60, enemies=None, damage=0.25):
        self.origin = (x1, y1)
        self.target = (x2, y2)
        dx = x2 - x1
        dy = y2 - y1
        self.angle = math.atan2(dy, dx) if (dx or dy) else 0
        self.distance = math.hypot(dx, dy)
        self.enemies = enemies or []
        self.damage = damage
        self._damaged_ids = set()
        # Explosión eléctrica al destino
        self.explosion = ElectricExplosion(x2, y2) if self.distance else None
        # Generar partículas del haz
        self.particles: list[Particle] = []
        for i in range(particle_count):
            t = i / particle_count
            px = x1 + t * dx + random.uniform(-4, 4)
            py = y1 + t * dy + random.uniform(-4, 4)
            color = random.choice([(0, 255, 255), (150, 255, 255), (255, 255, 255)])
            p = Particle(px, py, self.angle + random.uniform(-0.1, 0.1), 0, color, random.randint(2, 4), 5)
            self.particles.append(p)
        # Indicador de terminación
        self.finished = False

    def is_finished(self) -> bool:
        return self.finished