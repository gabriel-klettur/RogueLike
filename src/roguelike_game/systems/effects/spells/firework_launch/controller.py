# Path: src/roguelike_game/systems/combat/spells/firework_launch/controller.py
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
        model = self.model
        if model.finished:
            return
        # Avanzar el cohete
        dx = math.cos(model.angle) * model.speed
        dy = math.sin(model.angle) * model.speed
        model.x += dx
        model.y += dy
        model.traveled += math.hypot(dx, dy)
        # Generar estela de partículas
        for _ in range(4):
            angle_off = model.angle + random.uniform(-0.3, 0.3)
            speed = random.uniform(1, 2)
            color = random.choice([(255,255,255),(255,200,150)])
            size = random.randint(1,3)
            lifespan = 20
            pd = ParticleData(model.x, model.y, angle_off, speed, color, size, lifespan)
            model.particles.append(pd)
        # Actualizar partículas
        for pd in model.particles:
            pd.update_position()
        model.particles = [pd for pd in model.particles if not pd.is_dead()]
        # Verificar explosión
        if model.traveled >= model.distance:
            model.finished = True