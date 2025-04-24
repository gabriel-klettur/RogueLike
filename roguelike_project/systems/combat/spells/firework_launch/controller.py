# File: roguelike_project/systems/combat/spells/firework_launch/controller.py
import random
import math
from .model import FireworkLaunchModel, ParticleData

class FireworkLaunchController:
    """
    Controlador: gestiona el avance y la creación de partículas.
    """
    def __init__(self, model: FireworkLaunchModel):
        self.model = model

    def update(self):
        m = self.model
        if m.finished:
            return
        # Avanzar el cohete
        dx = math.cos(m.angle) * m.speed
        dy = math.sin(m.angle) * m.speed
        m.x += dx
        m.y += dy
        m.traveled += math.hypot(dx, dy)
        # Generar estela de partículas
        for _ in range(4):
            angle_off = m.angle + random.uniform(-0.3, 0.3)
            speed = random.uniform(1, 2)
            color = random.choice([(255,255,255),(255,200,150)])
            size = random.randint(1,3)
            lifespan = 20
            pd = ParticleData(m.x, m.y, angle_off, speed, color, size, lifespan)
            m.particles.append(pd)
        # Actualizar partículas
        for pd in m.particles:
            pd.update_position()
        m.particles = [pd for pd in m.particles if not pd.is_dead()]
        # Verificar explosión
        if m.traveled >= m.distance:
            m.finished = True