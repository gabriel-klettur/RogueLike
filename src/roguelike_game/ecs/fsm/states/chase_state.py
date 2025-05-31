import math
from roguelike_game.ecs.fsm.state import State
from roguelike_game.ecs.fsm.states.death_state import DeathState

from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE

class ChaseState(State):
    """
    Estado Chase: persigue activamente al jugador.
    """
    def enter(self, entity):
        # Se podría iniciar animación de correr
        pass

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Resetear velocidad antes de moverse
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = player_pos.x - pos.x
        dy = player_pos.y - pos.y
        dist_sq = dx*dx + dy*dy
        # Actualizar animación de chase según dirección
        anim = world.components['Animator'][eid]
        if abs(dx) > abs(dy):
            direction = 'left' if dx < 0 else 'right'
        else:
            direction = 'down' if dy > 0 else 'up'
        anim.current_state = f"chase_{direction}"
        # Si jugador sale de rango de aggro, volver a Idle
        aggro_radius = world.components['AggroRange'][entity].radius * TILE_SIZE
        if dist_sq > aggro_radius**2:
            npc_state = world.components['NPCState'][entity]
            from roguelike_game.ecs.fsm.states.patrol_state import PatrolState
            npc_state.fsm.change_state(PatrolState(), entity)
            return
        speed_cmp = world.components['MovementSpeed'][eid]
        step = speed_cmp.speed * dt if dt else speed_cmp.speed
        if dist_sq > step*step:
            dist = math.sqrt(dist_sq)
            # Aplicar velocidad; MovementCollisionSystem resolverá colisiones
            vx = dx/dist * step
            vy = dy/dist * step
            world.components['Velocity'][eid] = Velocity(vx, vy)
        else:
            # Detener al alcanzar rango
            world.components['Velocity'][eid] = Velocity(0, 0)

    def exit(self, entity):
        # Detener movimiento al salir de ChaseState
        world = entity.world
        world.components['Velocity'][entity.id] = Velocity(0, 0)
