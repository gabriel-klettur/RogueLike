import math
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.idle_state import IdleState
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_engine.config.config_tiles import TILE_SIZE

class PatrolState(State):
    """
    Estado Patrol: recorre waypoints definidos en PatrolRoute.
    """
    def __init__(self):
        self.current_index = 0

    def enter(self, entity):
        self.current_index = 0

    def execute(self, entity, dt):
        world = entity.world
        # Verificar muerte
        hp_cmp = world.components['Health'][entity]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][entity]
        route = world.components['PatrolRoute'][entity]
        speed_cmp = world.components['MovementSpeed'][entity]
        # Detectar jugador y cambiar a AggroState
        player_pos = world.player_position
        if player_pos:
            dx_p = pos.x - player_pos.x
            dy_p = pos.y - player_pos.y
            if dx_p*dx_p + dy_p*dy_p <= (world.components['AggroRange'][entity].radius * TILE_SIZE) ** 2:
                npc_state = world.components['NPCState'][entity]
                npc_state.fsm.change_state(AggroState(), entity)
                return
        # Mover hacia el waypoint actual
        if self.current_index < len(route.points):
            tx, ty = route.points[self.current_index]
            dx = tx - pos.x
            dy = ty - pos.y
            dist_sq = dx*dx + dy*dy
            step = speed_cmp.speed * dt if dt else speed_cmp.speed
            if dist_sq <= step*step:
                self.current_index += 1
            else:
                dist = math.sqrt(dist_sq)
                pos.x += dx/dist * step
                pos.y += dy/dist * step
        else:
            # Ruta completa, volver a IdleState
            npc_state = world.components['NPCState'][entity]
            npc_state.fsm.change_state(IdleState(), entity)

    def exit(self, entity):
        pass