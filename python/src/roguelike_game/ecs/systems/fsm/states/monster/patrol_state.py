import math
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState
from roguelike_game.ecs.systems.fsm.anim_bridge import (
    set_mapped_anim,
    primary_direction_from_vector,
)
from roguelike_game.ecs.utils.position_utils import compute_entity_center

class PatrolState(State):
    """
    Estado Patrol: recorre waypoints definidos en PatrolRoute.
    Soporta pausas (dwell) por waypoint si `PatrolRoute.dwell_times` está definido.
    """
    def __init__(self):
        self.current_index = 0
        self.waiting = False
        self.dwell_timer = 0.0

    def enter(self, entity):
        self.current_index = 0
        self.waiting = False
        self.dwell_timer = 0.0
        # Asegurar cambio inmediato a assets de patrulla al entrar
        try:
            set_mapped_anim(entity, 'PatrolState', None)
        except Exception:
            pass

    def execute(self, entity, dt):
        world = entity.world
        eid = entity.id
        # Resetear velocidad antes de mover
        world.components['Velocity'][eid] = Velocity(0, 0)
        # Verificar muerte
        hp_cmp = world.components['Health'][eid]
        if hp_cmp.current_hp <= 0:
            # Import local para evitar importación circular con UnconsciousState
            from roguelike_game.ecs.systems.fsm.states.unconscious_state import UnconsciousState
            world.components['NPCState'][eid].fsm.change_state(UnconsciousState(), entity)
            return
        # Asegurar componentes requeridos para patrulla
        comps = world.components
        if ('PatrolRoute' not in comps or eid not in comps['PatrolRoute'] or
            'Position' not in comps or eid not in comps['Position'] or
            'MovementSpeed' not in comps or eid not in comps['MovementSpeed']):
            # Sin ruta/posición/velocidad: detener. Si IdleState está permitido, transicionar; si no, permanecer en Patrol para evitar spam.
            try:
                comps['Velocity'][eid] = Velocity(0, 0)
            except Exception:
                pass
            npc_state = comps.get('NPCState', {}).get(eid)
            if npc_state:
                fsm = getattr(npc_state, 'fsm', None)
                allowed = getattr(fsm, 'context', {}).get('allowed_state_classes') if fsm else None
                if not allowed or 'IdleState' in allowed:
                    from roguelike_game.ecs.systems.fsm.states.idle_state import IdleState
                    npc_state.fsm.change_state(IdleState(), entity)
                else:
                    # Idle no permitido: mantener Patrol detenido y animación base
                    try:
                        set_mapped_anim(entity, 'PatrolState', None)
                    except Exception:
                        pass
            return
        pos = world.components['Position'][eid]
        route = world.components['PatrolRoute'][eid]
        speed_cmp = world.components['MovementSpeed'][eid]

        # Detectar jugador y cambiar a AggroState (usar centros como en ChaseState)
        player_pos = world.player_position
        rng_cmp = world.components.get('AggroRange', {}).get(eid)
        if player_pos and rng_cmp:
            try:
                player_id = world.player_entity
                ph = world.components.get('Health', {}).get(player_id)
                player_dead = (ph is None) or (ph.current_hp <= 0)
                has_death_timer = player_id in world.components.get('DeathTimer', {})
                if player_dead or has_death_timer:
                    pass
                else:
                    comps = world.components
                    pos_map = comps.get('Position', {})
                    spr_map = comps.get('Sprite', {})
                    scl_map = comps.get('Scale', {})
                    ppos = pos_map.get(player_id)
                    # Calcular centros para NPC y Player (con fallback a posicion base)
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
                        if dspr and ppos is not None:
                            dc = compute_entity_center(ppos, dspr, dscl)
                            x2, y2 = float(dc.x), float(dc.y)
                        else:
                            x2, y2 = (float(player_pos.x), float(player_pos.y)) if player_pos else (0.0, 0.0)
                    except Exception:
                        x1, y1 = float(pos.x), float(pos.y)
                        x2, y2 = (float(player_pos.x), float(player_pos.y)) if player_pos else (0.0, 0.0)
                    dx_p = x1 - x2
                    dy_p = y1 - y2
                    if dx_p*dx_p + dy_p*dy_p <= (rng_cmp.radius * TILE_SIZE) ** 2:
                        npc_state = world.components.get('NPCState', {}).get(eid)
                        if npc_state:
                            npc_state.fsm.change_state(AggroState(), entity)
                        return
            except Exception:
                pass

        # Si está esperando en un waypoint, consumir dwell y mantener anim/velocidad
        if self.waiting:
            dt_val = dt or 0.0
            self.dwell_timer -= dt_val
            # mantener detenido
            world.components['Velocity'][eid] = Velocity(0, 0)
            if self.dwell_timer <= 0.0:
                self.waiting = False
                self.current_index += 1
            else:
                # mantener anim base durante la espera
                try:
                    set_mapped_anim(entity, 'PatrolState', None)
                except Exception:
                    pass
                return

        # Mover hacia el waypoint actual
        if self.current_index < len(route.points):
            tx, ty = route.points[self.current_index]
            dx = tx - pos.x
            dy = ty - pos.y
            dist_sq = dx*dx + dy*dy
            step = speed_cmp.speed * dt if dt else speed_cmp.speed
            if dist_sq <= step*step:
                # Al llegar al waypoint, activar espera si aplica
                dwell_list = getattr(route, 'dwell_times', None)
                dwell = 0.0
                if dwell_list and self.current_index < len(dwell_list):
                    try:
                        dwell = float(dwell_list[self.current_index])
                    except Exception:
                        dwell = 0.0
                if dwell > 0.0:
                    self.waiting = True
                    self.dwell_timer = dwell
                    # Detener y mostrar anim base mientras espera
                    world.components['Velocity'][eid] = Velocity(0, 0)
                    try:
                        set_mapped_anim(entity, 'PatrolState', None)
                    except Exception:
                        pass
                else:
                    # Sin espera, avanzar al siguiente punto
                    self.current_index += 1
                    world.components['Velocity'][eid] = Velocity(0, 0)
                    try:
                        set_mapped_anim(entity, 'PatrolState', None)
                    except Exception:
                        pass
            else:
                dist = math.sqrt(dist_sq)
                vx = dx/dist * step
                vy = dy/dist * step
                world.components['Velocity'][eid] = Velocity(vx, vy)
                # Actualizar animación de patrulla según dirección vía anim_map
                try:
                    direction = primary_direction_from_vector(dx, dy)
                    set_mapped_anim(entity, 'PatrolState', direction)
                except Exception:
                    pass
        else:
            # Ruta completa, reiniciar para patrulla en bucle
            world.components['Velocity'][eid] = Velocity(0, 0)
            self.current_index = 0
            self.waiting = False
            self.dwell_timer = 0.0
            # Asegurar anim base de patrulla
            try:
                set_mapped_anim(entity, 'PatrolState', None)
            except Exception:
                pass

    def exit(self, entity):
        pass