import math
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim, primary_direction_from_vector
from roguelike_game.ecs.utils.position_utils import compute_entity_center


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
            # Import local para evitar importación circular con UnconsciousState
            from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
            world.components['NPCState'][eid].fsm.change_state(UnconsciousState(), entity)
            return
        # Si el jugador está inconsciente (HP<=0) o ya tiene DeathTimer, no perseguir
        try:
            player_id = world.player_entity
            ph = world.components.get('Health', {}).get(player_id)
            player_dead = (ph is None) or (ph.current_hp <= 0)
            has_death_timer = player_id in world.components.get('DeathTimer', {})
            if player_dead or has_death_timer:
                from roguelike_game.ecs.systems.fsm.states.monster.patrol_state import PatrolState
                world.components['NPCState'][eid].fsm.change_state(PatrolState(), entity)
                return
        except Exception:
            pass
        comps = world.components
        pos_map = comps.get('Position', {})
        spr_map = comps.get('Sprite', {})
        scl_map = comps.get('Scale', {})
        pos = pos_map.get(eid)
        if pos is None:
            return
        player_id = getattr(world, 'player_entity', None)
        ppos = pos_map.get(player_id) if player_id is not None else None
        if ppos is None:
            return
        # Compute centers for NPC and Player
        try:
            aspr = spr_map.get(eid)
            ascl = scl_map.get(eid)
            if aspr:
                ac = compute_entity_center(pos, aspr, ascl)
                x1, y1 = float(ac.x), float(ac.y)
            else:
                x1, y1 = float(pos.x), float(pos.y)
            dspr = spr_map.get(player_id)
            dscl = scl_map.get(player_id)
            if dspr:
                dc = compute_entity_center(ppos, dspr, dscl)
                x2, y2 = float(dc.x), float(dc.y)
            else:
                x2, y2 = float(ppos.x), float(ppos.y)
        except Exception:
            x1, y1 = float(pos.x), float(pos.y)
            x2, y2 = float(ppos.x), float(ppos.y)
        dx = x2 - x1
        dy = y2 - y1
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
        mr_cmp = world.components['MeleeRange'][eid]
        melee_dist_sq = (mr_cmp.range * TILE_SIZE) ** 2
        # If within melee range by center distance, switch to AttackState
        if dist_sq <= melee_dist_sq:
            # Import local para evitar importación circular con AttackState
            from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
            world.components['NPCState'][eid].fsm.change_state(AttackState(), entity)
            return
        # Si jugador sale de rango de aggro, volver a patrulla SOLO si no hay área de defensa
        has_defend = world.components.get('DefendArea', {}).get(eid) is not None
        if not has_defend:
            aggro_radius = world.components['AggroRange'][eid].radius * TILE_SIZE
            if dist_sq > aggro_radius**2:            
                npc_state = world.components['NPCState'][eid]            
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