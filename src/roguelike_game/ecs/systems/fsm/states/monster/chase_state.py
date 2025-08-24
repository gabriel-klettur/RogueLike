import math
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim, primary_direction_from_vector


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
            # Import local para evitar importación circular con DeathState
            from roguelike_game.ecs.systems.fsm.states.death_state import DeathState
            world.components['NPCState'][entity].fsm.change_state(DeathState(), entity)
            return
        pos = world.components['Position'][entity]
        player_pos = world.player_position
        if not player_pos:
            return
        dx = player_pos.x - pos.x
        dy = player_pos.y - pos.y
        dist_sq = dx*dx + dy*dy
        # Leash: no salir del área defendida si existe y leash está activo
        try:
            defend_cmp = world.components.get('DefendArea', {}).get(eid)
            if defend_cmp is not None and bool(getattr(defend_cmp, 'leash', True)):
                cx = float(getattr(defend_cmp, 'center_x', 0.0))
                cy = float(getattr(defend_cmp, 'center_y', 0.0))
                r = float(getattr(defend_cmp, 'radius_px', 0.0))
                shape = str(getattr(defend_cmp, 'shape', 'circle') or 'circle').lower()
                ddx = pos.x - cx
                ddy = pos.y - cy
                tol = 1.05
                if shape == 'square':
                    # Fuera si excede media-lado en cualquiera de los ejes (con tolerancia)
                    if abs(ddx) > r * tol or abs(ddy) > r * tol:
                        from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                        world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
                        return
                else:
                    if ddx*ddx + ddy*ddy > (r * tol) * (r * tol):
                        # Volver a patrullar si se sale ligeramente del radio
                        from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                        world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
                        return
        except Exception:
            pass
        # Actualizar animación de chase según dirección vía anim_map
        direction = primary_direction_from_vector(dx, dy)
        set_mapped_anim(entity, 'ChaseState', direction)

        # Si dentro de rango melee: cambiar a AttackState
        mr_cmp = world.components['MeleeRange'][entity]
        melee_dist_sq = (mr_cmp.range * TILE_SIZE) ** 2
        dx = world.player_position.x - world.components['Position'][entity].x
        dy = world.player_position.y - world.components['Position'][entity].y
        if dx*dx + dy*dy <= melee_dist_sq:            
            # Import local para evitar importación circular con AttackState
            from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
            world.components['NPCState'][eid].fsm.change_state(AttackState(), entity)
            return
        # Si jugador sale de rango de aggro, volver a patrulla SOLO si no hay área de defensa
        has_defend = world.components.get('DefendArea', {}).get(eid) is not None
        if not has_defend:
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
        # Al salir de ChaseState, detener movimiento; siguiente estado decidirá animación
        world = entity.world
        eid = entity.id
        world.components['Velocity'][eid] = Velocity(0, 0)