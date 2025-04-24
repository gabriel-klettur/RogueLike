# File: roguelike_project/systems/combat/spells/laser_beam/controller.py
import math
from .model import LaserBeamModel

class LaserBeamController:
    """
    Controlador: actualiza estado del modelo cada frame.
    """
    def __init__(self, model: LaserBeamModel):
        self.model = model

    def update(self):
        m = self.model
        # Actualizar partículas
        for p in m.particles:
            p.update()
        m.particles = [p for p in m.particles if p.age < p.lifespan]
        # Actualizar explosión
        if m.explosion:
            m.explosion.update()
        # Colisiones con enemigos
        for enemy in m.enemies:
            if not enemy.alive or id(enemy) in m._damaged_ids:
                continue
            ex = enemy.x + enemy.sprite_size[0] / 2
            ey = enemy.y + enemy.sprite_size[1] / 2
            x1, y1 = m.origin
            x2, y2 = m.target
            dx, dy = x2 - x1, y2 - y1
            length_sq = dx * dx + dy * dy
            if length_sq == 0:
                continue
            t = ((ex - x1) * dx + (ey - y1) * dy) / length_sq
            t = max(0, min(1, t))
            closest_x = x1 + t * dx
            closest_y = y1 + t * dy
            dist = math.hypot(ex - closest_x, ey - closest_y)
            if dist <= 100:
                enemy.take_damage(m.damage)
                m._damaged_ids.add(id(enemy))
        # Marcar finalizado
        if not m.particles and (m.explosion is None or m.explosion.finished):
            m.finished = True