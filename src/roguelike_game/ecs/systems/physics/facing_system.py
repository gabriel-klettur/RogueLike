"""
Module: facing_system.py
Updates entity facing direction based on their Velocity component,
applying a cooldown between direction changes to prevent flickering.
"""
import time
import math

from roguelike_game.ecs.components.combat.facing_cooldown import FacingCooldown
from roguelike_engine.utils.benchmark import benchmark

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

class FacingSystem:
    """
    Sistema que actualiza el Animator.current_state basado en Velocity (4 direcciones)
    respetando un cooldown para evitar cambios demasiado rápidos.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, camera=None):
        """
        Recorre todas las entidades con Velocity y Animator, y:
          1. Ignora entidades sin Animator.
          2. Inicializa o recupera su FacingCooldown.
          3. Si la entidad se está moviendo y ha pasado el cooldown,
             calcula la nueva dirección cardinal (up/down/left/right)
             según la componente dominante de la velocidad.
          4. Actualiza Animator.current_state y reinicia el cooldown.
        """
        comps    = world.components
        vel_map  = comps.get('Velocity', {})
        anim_map = comps.get('Animator', {})
        fc_map   = comps.get('FacingCooldown', {})

        for eid, vel in vel_map.items():
            # 1) Requerimos que la entidad tenga un Animator
            animator = anim_map.get(eid)
            if not animator:
                continue
            # El jugador es controlado por PlayerFacingSystem; no sobreescribir sus animaciones aquí
            if eid in comps.get('PlayerTagComponent', {}):
                continue
            # No sobrescribir animaciones de chase para evitar parpadeo
            if animator.current_state.startswith('chase_'):
                continue
            # Respetar bases mapeadas para estados de persecución según anim_map por entidad
            npc_state = comps.get('NPCState', {}).get(eid)
            if npc_state is not None:
                try:
                    amap = (getattr(npc_state.fsm, 'context', {}) or {}).get('anim_map') or {}
                    chase_bases = []
                    for k in ('ChaseState', 'AlertChaseState'):
                        b = amap.get(k)
                        if b:
                            chase_bases.append(b)
                    if any(
                        animator.current_state == base or animator.current_state.startswith(f"{base}_")
                        for base in chase_bases
                    ):
                        continue
                except Exception:
                    pass

            # 2) Obtener o crear el cooldown de facing
            now = time.time()
            fc = fc_map.get(eid)
            if not fc:
                fc = FacingCooldown()
                fc_map[eid] = fc

            # Habilitar lógica de idle/walk solo para el jugador
            is_player = eid in comps.get('PlayerTagComponent', {})
            suffix_avail = is_player  # NPCs usarán solo estados base
            vx, vy = vel.vx, vel.vy
            if vx == 0 and vy == 0:
                # Entidad quieta: elegir estado idle o base
                if suffix_avail:
                    # Extraer dirección completa antes del sufijo (walk/idle)
                    base = animator.current_state.rsplit('_', 1)[0]
                    idle_key = f"{base}_idle"
                    new_state = idle_key if idle_key in animator.animations else base
                else:
                    new_state = animator.current_state
                if new_state != animator.current_state:
                    animator.current_state = new_state
                continue

            # 4) Respetar cooldown antes de cambiar facing
            if now < fc.next_allowed:
                continue

            # 5) Determinar nueva dirección y animación de caminata (8 direcciones)
            direction = get_direction_name(vx, vy)
            if suffix_avail:
                key = f"{direction}_walk"
                new_state = key if key in animator.animations else direction
            else:
                new_state = direction
            if new_state != animator.current_state:
                animator.current_state = new_state
                # Reiniciar cooldown de facing
                fc.next_allowed = now + 1.0