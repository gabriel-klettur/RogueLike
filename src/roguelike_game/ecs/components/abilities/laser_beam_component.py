# Path: src/roguelike_game/ecs/components/abilities/laser_beam_component.py
import time

class LaserBeamComponent:
    """
    Componente ECS para un haz láser: genera partículas a lo largo de una línea y aplica daño.
    origin: (x1, y1)
    target: (x2, y2)
    particle_count: número de partículas
    dispersion: dispersión de partículas
    colors: lista de colores RGB
    lifespan: duración de partículas (ticks)
    scale: escala visual del haz
    damage: daño aplicado
    """
    def __init__(self, x1, y1, x2, y2,
                 particle_count=60, dispersion=4,
                 colors=None, lifespan=5,
                 scale=1.0, damage=0.25, duration=None):
        self.origin = (x1, y1)
        self.target = (x2, y2)
        self.particle_count = particle_count
        self.dispersion = dispersion
        self.colors = colors or [(0, 255, 255), (150, 255, 255), (255, 255, 255)]
        self.lifespan = lifespan
        self.scale = scale
        self.damage = damage
        self.duration = duration
        self._damaged_ids = set()
        self.start_time = time.time()