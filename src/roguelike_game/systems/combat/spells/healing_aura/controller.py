# Path: src/roguelike_game/systems/combat/spells/healing_aura/controller.py
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
        model = self.model
        # Incrementa contador de frames
        model.timer += 1
        # Genera nuevas partículas si aún está dentro de la duración
        if model.timer <= model.duration:
            for _ in range(3):
                # Generación distribuida radial
                angle = random.uniform(0, 2 * math.pi)
                radius = random.uniform(0, 1) ** 0.5
                half_w = model.player.sprite_size[0] / 2
                offset_x = math.cos(angle) * radius * half_w
                model.particles.append(HealingParticle(model.player, offset_x))
        # Actualiza y limpia partículas muertas
        for p in model.particles:
            p.update()
        model.particles = [p for p in model.particles if not p.is_dead()]