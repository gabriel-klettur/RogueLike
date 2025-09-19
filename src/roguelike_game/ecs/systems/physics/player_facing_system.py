import pygame
from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.ecs.systems.fsm.anim_bridge import set_mapped_anim_for

def get_direction_name(dx, dy):
    """Return one of 8 directions based on vector dx, dy."""
    angle = math.degrees(math.atan2(-dy, dx)) % 360
    if angle < 22.5 or angle >= 337.5:
        return 'right'
    elif angle < 67.5:
        return 'up_right'
    elif angle < 112.5:
        return 'up'
    elif angle < 157.5:
        return 'up_left'
    elif angle < 202.5:
        return 'left'
    elif angle < 247.5:
        return 'down_left'
    elif angle < 292.5:
        return 'down'
    else:
        return 'down_right'

class PlayerFacingSystem:
    """
    Sistema que actualiza Animator.current_state para el jugador
    basándose en la posición del ratón y su velocidad (idle/walk).
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        # Estado para decidir la fuente dominante del apuntado (stick/mouse)
        self._last_source: dict[int, str] = {}  # eid -> 'stick' | 'mouse'
        self._last_stick_vec: dict[int, tuple[float, float]] = {}  # eid -> (ax, ay) normalizado
        self._prev_mouse_pos: tuple[int, int] | None = None
        self.aim_deadzone = 0.25
    
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        vel_map = comps.get('Velocity', {})
        anim_map = comps.get('Animator', {})
        players = comps.get('PlayerTagComponent', {})
        input_map = comps.get('InputComponent', {})
        AIM_DEADZONE = 0.25
        
        # Detectar movimiento del ratón (global)
        mx, my = pygame.mouse.get_pos()
        mouse_moved = (self._prev_mouse_pos != (mx, my))
        self._prev_mouse_pos = (mx, my)

        for eid in players:
            animator = anim_map.get(eid)
            pos = pos_map.get(eid)
            if not animator or not pos or camera is None:
                continue
            # Respetar animaciones accionadas por FSM (ataque/daño/muerte) usando el mapa de animaciones
            try:
                fsm = comps.get('NPCState', {}).get(eid).fsm
                amap = (getattr(fsm, 'context', {}) or {}).get('anim_map') or {}
                action_bases = []
                for k in ('PlayerAttackState', 'AttackState', 'DamageState', 'DeathState'):
                    b = amap.get(k)
                    if b:
                        action_bases.append(b)
                if any(
                    animator.current_state == base
                    or animator.current_state.startswith(f"{base}_")
                    or animator.current_state.endswith(f"_{base}")
                    for base in action_bases
                ):
                    # No sobreescribir animación de acción activa
                    continue
            except Exception:
                pass
            vel = vel_map.get(eid)
            vx = vel.vx if vel else 0
            vy = vel.vy if vel else 0
            # 1) Intentar usar el aim del stick derecho si supera el deadzone
            inp = input_map.get(eid)
            direction = None
            if inp is not None:
                ax = float(getattr(inp, 'aim_x', 0.0) or 0.0)
                ay = float(getattr(inp, 'aim_y', 0.0) or 0.0)
                stick_active = (ax*ax + ay*ay) >= (self.aim_deadzone * self.aim_deadzone)
                if stick_active:
                    # Normalizar y recordar como última dirección válida de stick
                    mag = (ax*ax + ay*ay) ** 0.5
                    if mag > 0:
                        nx, ny = ax / mag, ay / mag
                        self._last_stick_vec[eid] = (nx, ny)
                    self._last_source[eid] = 'stick'
                elif mouse_moved:
                    self._last_source[eid] = 'mouse'

            # 2) Resolver la dirección según la fuente dominante
            src = self._last_source.get(eid)
            if src == 'stick':
                # Usar el último vector de stick conocido, aunque el stick esté en reposo
                nx, ny = self._last_stick_vec.get(eid, (0.0, 0.0))
                if nx == 0.0 and ny == 0.0:
                    # Fallback duro si no tenemos vector aún
                    world_x = mx / camera.zoom + camera.offset_x
                    world_y = my / camera.zoom + camera.offset_y
                    dx = world_x - pos.x
                    dy = world_y - pos.y
                    direction = get_direction_name(dx, dy)
                else:
                    direction = get_direction_name(nx, ny)
            else:
                # Por defecto o si la fuente dominante es el ratón
                world_x = mx / camera.zoom + camera.offset_x
                world_y = my / camera.zoom + camera.offset_y
                dx = world_x - pos.x
                dy = world_y - pos.y
                direction = get_direction_name(dx, dy)
            
            # Determinar estado base a través del mapa de animaciones y aplicarlo
            state_class = 'IdleState' if vx == 0 and vy == 0 else 'MoveState'
            set_mapped_anim_for(world, eid, state_class, direction)