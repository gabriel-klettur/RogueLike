import math
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed
from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_engine.config.config_tiles import TILE_SIZE

class ChaseState(State):
    """
    Estado Chase: persigue activamente al jugador.
    """
    def enter(self, entity):
        # Se podría iniciar animación de correr
        pass

    def execute(self, entity, dt):
        from roguelike_game.ecs.fsm.states.idle_state import IdleState
        world = entity.world
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = player_pos.x - pos.x
        dy = player_pos.y - pos.y
        dist_sq = dx*dx + dy*dy
        # Si jugador sale de rango de aggro, volver a Idle
        aggro_radius = world.components['AggroRange'][entity].radius * TILE_SIZE
        if dist_sq > aggro_radius**2:
            npc_state = world.components['NPCState'][entity]
            npc_state.fsm.change_state(IdleState(), entity)
            return
        speed_cmp = world.components['MovementSpeed'][entity]
        step = speed_cmp.speed * dt if dt else speed_cmp.speed
        if dist_sq > step*step:
            dist = math.sqrt(dist_sq)
            pos.x += dx/dist * step
            pos.y += dy/dist * step

    def exit(self, entity):
        # Limpiar animación de correr
        pass
