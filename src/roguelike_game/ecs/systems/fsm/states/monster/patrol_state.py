import math
import random
from roguelike_game.ecs.systems.fsm.state import State
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.fsm.states.monster.aggro_state import AggroState
from roguelike_game.ecs.systems.fsm.anim_bridge import (
    set_mapped_anim,
    primary_direction_from_vector,
)

class PatrolState(State):
    """
    Estado Patrol: recorre waypoints definidos en PatrolRoute.
    Soporta pausas (dwell) por waypoint si `PatrolRoute.dwell_times` está definido.
    """
    def __init__(self):
        self.current_index = 0
        self.waiting = False
        self.dwell_timer = 0.0
        self.natural_target = None  # (tx, ty) for dynamic 'natural' pattern

    def enter(self, entity):
        self.current_index = 0
        self.waiting = False
        self.dwell_timer = 0.0
        self.natural_target = None
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
        # Rama dinámica para patrón 'natural'
        if getattr(route, 'pattern_id', None) == 'natural':
            self._execute_natural(entity, dt, pos, speed_cmp, route)
            return
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

    def _execute_natural(self, entity, dt, pos, speed_cmp, route):
        """
        Patrón natural: esperar 1s y luego elegir un nuevo objetivo dentro del radio.
        Distancia mínima a caminar: radius / 4 (route.min_step).
        """
        world = entity.world
        eid = entity.id

        # Extraer metadatos con valores por defecto seguros
        cx, cy = (route.area_center if getattr(route, 'area_center', None) else (pos.x, pos.y))
        radius = float(getattr(route, 'area_radius', 0.0) or 0.0)
        min_step = float(getattr(route, 'min_step', 0.0) or 0.0)

        # Si estamos esperando, consumir dwell de 1s
        if self.waiting:
            self.dwell_timer -= (dt or 0.0)
            world.components['Velocity'][eid] = Velocity(0, 0)
            if self.dwell_timer <= 0.0:
                self.waiting = False
                # listo para elegir nuevo objetivo
            else:
                try:
                    set_mapped_anim(entity, 'PatrolState', None)
                except Exception:
                    pass
                return

        # Si no estamos esperando y aún no tenemos objetivo, seleccionar uno (moverse inmediatamente al inicio)
        if self.natural_target is None and not self.waiting:
            def rand_in_disk():
                u = random.random()
                v = random.random()
                ang = 2.0 * math.pi * v
                r = radius * math.sqrt(u)
                return cx + r * math.cos(ang), cy + r * math.sin(ang)

            attempts = 0
            max_attempts = 200
            while attempts < max_attempts:
                attempts += 1
                tx, ty = rand_in_disk()
                dx = tx - pos.x
                dy = ty - pos.y
                if dx*dx + dy*dy >= (min_step * min_step):
                    self.natural_target = (tx, ty)
                    break
            if self.natural_target is None:
                # Fallback: punto en el borde al Este del centro
                self.natural_target = (cx + radius, cy)

        # Mover hacia el objetivo actual
        tx, ty = self.natural_target
        dx = tx - pos.x
        dy = ty - pos.y
        dist_sq = dx*dx + dy*dy
        step = speed_cmp.speed * dt if dt else speed_cmp.speed
        if dist_sq <= step*step:
            # Llegó: detener y esperar 1s antes de elegir otro
            self.natural_target = None
            self.waiting = True
            self.dwell_timer = 1.0
            world.components['Velocity'][eid] = Velocity(0, 0)
            try:
                set_mapped_anim(entity, 'PatrolState', None)
            except Exception:
                pass
            return
        else:
            dist = math.sqrt(dist_sq)
            vx = dx/dist * step
            vy = dy/dist * step
            world.components['Velocity'][eid] = Velocity(vx, vy)
            try:
                direction = primary_direction_from_vector(dx, dy)
                set_mapped_anim(entity, 'PatrolState', direction)
            except Exception:
                pass

    def exit(self, entity):
        pass