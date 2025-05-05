# Path: src/roguelike_game/entities/npc/types/monster/controller.py

from src.roguelike_game.entities.npc.base.controller import BaseNPCController
from src.roguelike_game.entities.npc.types.monster.model import MonsterModel
from src.roguelike_game.entities.npc.utils.geometry import calculate_distance

class MonsterController(BaseNPCController):
    def __init__(self, model: MonsterModel):
        super().__init__(model)
        # ← Usamos el mismo NPCMovement que creó el modelo
        self.movement = model.movement

    def update(self, state):
        m = self.model
        if not m.alive:
            return

        px, py = state.player.x, state.player.y
        dist = calculate_distance(m.x, m.y, px, py)

        if dist <= 500:
            self._follow_player(px, py, state)
        else:
            self._patrol(state)

    def _follow_player(self, px, py, state):
        m = self.model
        dx, dy = px - m.x, py - m.y
        # Mover con chequeo de colisión contra el mapa y obstáculos
        self.movement.move(dx, dy, state.tiles, state.obstacles)

    def _patrol(self, state):
        m = self.model
        dx, dy, length = m.path[m.current_step]
        # Igual: mueve usando colisión
        self.movement.move(dx, dy, state.tiles, state.obstacles)
        # Actualizar progreso de la ruta aunque no se mueva (puedes ajustar esto)
        m.step_progress += m.speed
        m.direction = (dx, dy)
        if m.step_progress >= length:
            m.current_step = (m.current_step + 1) % len(m.path)
            m.step_progress = 0.0
