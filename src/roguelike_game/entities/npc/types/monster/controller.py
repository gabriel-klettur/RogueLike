# Path: src/roguelike_game/entities/npc/types/monster/controller.py
import math
from roguelike_game.entities.npc.base.controller import BaseNPCController
from roguelike_game.entities.npc.types.monster.model import MonsterModel
from roguelike_game.entities.npc.utils.geometry import calculate_distance

class MonsterController(BaseNPCController):
    """
    Controlador de monstruos: patrulla hasta que detecta al jugador,
    entonces persigue hasta que sus hitboxes de pies colisionan.
    """
    def __init__(self, model: MonsterModel):
        super().__init__(model)
        # Reutilizamos el componente de movimiento del modelo
        self.movement = model.movement

    def update(self, state, map):
        m = self.model
        if not m.alive:
            return

        # Posición del jugador
        px, py = state.player.x, state.player.y
        dist = calculate_distance(m.x, m.y, px, py)

        # Si está lo bastante cerca, perseguir; si no, patrullar
        if dist <= 500:
            self._follow_player(px, py, state, map)
        else:
            self._patrol(state, map)

    def _follow_player(self, px, py, state, map):
        """
        Persigue al jugador, pero se detiene justo en el momento
        en que la hitbox de pies del NPC va a colisionar con la del jugador.
        """
        m = self.model
        # Dirección normalizada hacia el jugador
        dx, dy = px - m.x, py - m.y
        norm = math.hypot(dx, dy)
        if norm == 0:
            return
        dx, dy = dx / norm, dy / norm

        # Calculamos la hitbox futura de los pies
        nx = m.x + dx * m.speed
        ny = m.y + dy * m.speed
        future_hitbox = self.movement.hitbox(nx, ny)

        # Hitbox actual del jugador
        player_hitbox = state.player.hitbox()

        # Si se solaparían, no avanzamos más
        if future_hitbox.colliderect(player_hitbox):
            return

        # En otro caso, movemos normalmente (respeta muros y obstáculos)
        self.movement.move(dx, dy, map.tiles_in_region, state.obstacles)

    def _patrol(self, state, map):
        """
        Patrulla siguiendo un camino predefinido en el modelo.
        """
        m = self.model
        dx, dy, length = m.path[m.current_step]
        # Mover con colisión
        self.movement.move(dx, dy, map.tiles_in_region, state.obstacles)
        # Actualizar progreso del paso
        m.step_progress += m.speed
        m.direction = (dx, dy)
        if m.step_progress >= length:
            m.current_step = (m.current_step + 1) % len(m.path)
            m.step_progress = 0.0
