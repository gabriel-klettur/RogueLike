# File: roguelike_project/systems/combat/spells/healing_aura/controller.py
import random
import math
from .model import HealingAuraModel, HealingParticle

class HealingAuraController:
    """
    Controlador: actualiza el modelo cada frame.
    """
    def __init__(self, model: HealingAuraModel, clock):
        self.model = model
        self.clock = clock

    def update(self):
        m = self.model
        # Incrementa contador de frames
        m.timer += 1
        # Genera nuevas partículas si aún está dentro de la duración
        if m.timer <= m.duration:
            for _ in range(3):
                # Generación distribuida radial
                angle = random.uniform(0, 2 * math.pi)
                radius = random.uniform(0, 1) ** 0.5
                half_w = m.player.sprite_size[0] / 2
                offset_x = math.cos(angle) * radius * half_w
                m.particles.append(HealingParticle(m.player, offset_x))
        # Actualiza y limpia partículas muertas
        for p in m.particles:
            p.update()
        m.particles = [p for p in m.particles if not p.is_dead()]