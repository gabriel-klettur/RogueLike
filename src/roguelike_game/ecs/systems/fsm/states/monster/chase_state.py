import math
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState


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
        # Si dentro de rango melee: cambiar a AttackState
        mr_cmp = world.components['MeleeRange'][entity]
        melee_dist_sq = (mr_cmp.range * TILE_SIZE) ** 2
        dx = world.player_position.x - world.components['Position'][entity].x
        dy = world.player_position.y - world.components['Position'][entity].y
        if dx*dx + dy*dy <= melee_dist_sq:            
            world.components['NPCState'][eid].fsm.change_state(AttackState(), entity)
            return
        # Si jugador sale de rango de aggro, volver a Idle
        aggro_radius = world.components['AggroRange'][entity].radius * TILE_SIZE
        if dist_sq > aggro_radius**2:            
            npc_state = world.components['NPCState'][entity]            
            from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
            npc_state.fsm.change_state(PatrolState(), entity)
            return
        speed_cmp = world.components['MovementSpeed'][eid]
        # Aumentar 50% de velocidad en chase
        chase_speed = speed_cmp.speed * 1.5
        step = chase_speed * dt if dt else chase_speed
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
        # Al salir de ChaseState, restablecer animación base y detener movimiento
        world = entity.world
        eid = entity.id
        # Detener movimiento
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Restablecer animación base eliminando 'chase_' si existía
        anim = world.components['Animator'][eid]
        if anim.current_state.startswith('chase_'):
            anim.current_state = anim.current_state[len('chase_'):]