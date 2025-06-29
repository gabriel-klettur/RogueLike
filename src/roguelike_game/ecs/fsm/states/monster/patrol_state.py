# Path: src/roguelike_game/ecs/fsm/states/monster/patrol_state.py
import math
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.death_state import DeathState
from roguelike_game.ecs.components.transform.velocity import Velocity
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
        eid = entity.id
        # Resetear velocidad antes de mover
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][eid]
        route = world.components['PatrolRoute'][eid]
        speed_cmp = world.components['MovementSpeed'][eid]
        # Detectar jugador y cambiar a AggroState
        player_pos = world.player_position
        if player_pos:
            dx_p = pos.x - player_pos.x
            dy_p = pos.y - player_pos.y
            if dx_p*dx_p + dy_p*dy_p <= (world.components['AggroRange'][eid].radius * TILE_SIZE) ** 2:
                from roguelike_game.ecs.fsm.states.monster.aggro_state import AggroState
                npc_state = world.components['NPCState'][eid]
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
                # Detener al llegar al waypoint
                world.components['Velocity'][eid] = Velocity(0, 0)
            else:
                dist = math.sqrt(dist_sq)
                vx = dx/dist * step
                vy = dy/dist * step
                world.components['Velocity'][eid] = Velocity(vx, vy)
        else:
            # Ruta completa, reiniciar para patrulla en bucle
            world.components['Velocity'][eid] = Velocity(0, 0)
            self.current_index = 0

    def exit(self, entity):
        pass