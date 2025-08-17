import math
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState

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
            # Import local para evitar importación circular con DeathState
            from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
            world.components['NPCState'][eid].fsm.change_state(DeathState(), entity)
            return
        # Asegurar componentes requeridos para patrulla
        comps = world.components
        if ('PatrolRoute' not in comps or eid not in comps['PatrolRoute'] or
            'Position' not in comps or eid not in comps['Position'] or
            'MovementSpeed' not in comps or eid not in comps['MovementSpeed']):
            # Sin ruta/posición/velocidad: detener y retroceder a Idle
            try:
                comps['Velocity'][eid] = Velocity(0, 0)
            except Exception:
                pass
            npc_state = comps.get('NPCState', {}).get(eid)
            if npc_state:
                from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
                npc_state.fsm.change_state(IdleState(), entity)
            return
        pos = world.components['Position'][eid]
        route = world.components['PatrolRoute'][eid]
        speed_cmp = world.components['MovementSpeed'][eid]
        # Detectar jugador y cambiar a AggroState
        player_pos = world.player_position
        rng_cmp = world.components.get('AggroRange', {}).get(eid)
        if player_pos and rng_cmp:
            dx_p = pos.x - player_pos.x
            dy_p = pos.y - player_pos.y
            if dx_p*dx_p + dy_p*dy_p <= (rng_cmp.radius * TILE_SIZE) ** 2:
                npc_state = world.components.get('NPCState', {}).get(eid)
                if npc_state:
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