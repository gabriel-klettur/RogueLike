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

    @benchmark(lambda self: self.perf_log, "4.2.2.PlayerFacingSystem.update")
    def update(self, world, camera=None):
        comps = world.components
        pos_map = comps.get('Position', {})
        vel_map = comps.get('Velocity', {})
        anim_map = comps.get('Animator', {})
        players = comps.get('PlayerTagComponent', {})
        
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
            mx, my = pygame.mouse.get_pos()
            world_x = mx / camera.zoom + camera.offset_x
            world_y = my / camera.zoom + camera.offset_y
            dx = world_x - pos.x
            dy = world_y - pos.y
            # calcular dirección basada en ratón (8 direcciones)
            direction = get_direction_name(dx, dy)
            # Determinar estado base a través del mapa de animaciones y aplicarlo
            state_class = 'IdleState' if vx == 0 and vy == 0 else 'MoveState'
            set_mapped_anim_for(world, eid, state_class, direction)