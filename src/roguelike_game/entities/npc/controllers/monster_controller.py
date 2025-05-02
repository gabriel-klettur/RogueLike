# Path: src/roguelike_game/entities/npc/controllers/monster_controller.py
from src.roguelike_game.entities.npc.interfaces import IController

class MonsterController(IController):
    def __init__(self, model):
        self.model = model

    def update(self, state):
        m = self.model
        if not m.alive:
            return

        px, py = state.player.x, state.player.y
        dist = m.calculate_distance(px, py)

        if dist <= 500:
            self._follow_player(px, py, dist)
        else:
            self._patrol()

    def _follow_player(self, px: float, py: float, dist: float):
        m = self.model
        dx = px - m.x
        dy = py - m.y
        # Si está muy cerca, solo giramos
        if dist < 250:
            m.direction = (-dx, -dy)
        else:
            nx, ny = dx / dist, dy / dist
            m.x += nx * m.speed
            m.y += ny * m.speed
            m.direction = (nx, ny)

    def _patrol(self):
        m = self.model
        dx, dy, dist_thresh = m.path[m.current_step]
        # Actualizamos posición
        m.x += dx * m.speed
        m.y += dy * m.speed
        m.step_progress += m.speed
        m.direction = (dx, dy)
        # Cambiar paso si recorrió suficiente
        if m.step_progress >= dist_thresh:
            m.current_step = (m.current_step + 1) % len(m.path)
            m.step_progress = 0.0