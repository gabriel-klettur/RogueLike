# Path: src/roguelike_game/entities/npc/types/monster/controller.py

from src.roguelike_game.entities.npc.base.controller import BaseNPCController
from src.roguelike_game.entities.npc.types.monster.model import MonsterModel
from src.roguelike_game.entities.npc.utils.geometry import calculate_distance

class MonsterController(BaseNPCController):
    def __init__(self, model: MonsterModel):
        super().__init__(model)

    def update(self, state):
        m = self.model
        if not m.alive:
            return

        px, py = state.player.x, state.player.y
        dist = calculate_distance(m.x, m.y, px, py)

        if dist <= 500:
            self._follow_player(px, py, dist)
        else:
            self._patrol()

    def _follow_player(self, px, py, dist):
        m = self.model
        dx, dy = px - m.x, py - m.y
        if dist < 250:
            m.direction = (-dx, -dy)
        else:
            nx, ny = dx / dist, dy / dist
            m.x += nx * m.speed
            m.y += ny * m.speed
            m.direction = (nx, ny)

    def _patrol(self):
        m = self.model
        dx, dy, length = m.path[m.current_step]
        m.x += dx * m.speed
        m.y += dy * m.speed
        m.step_progress += m.speed
        m.direction = (dx, dy)
        if m.step_progress >= length:
            m.current_step = (m.current_step + 1) % len(m.path)
            m.step_progress = 0.0
