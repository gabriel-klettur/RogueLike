"""
Module: facing_system.py
Updates entity facing direction based on their Velocity component,
applying a cooldown between direction changes to prevent flickering.
"""

import time

from roguelike_game.ecs.components.combat.facing_cooldown import FacingCooldown


class FacingSystem:
    """
    Sistema que actualiza el Animator.current_state basado en Velocity (4 direcciones)
    respetando un cooldown para evitar cambios demasiado rápidos.
    """

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

            # 2) Obtener o crear el cooldown de facing
            now = time.time()
            fc = fc_map.get(eid)
            if not fc:
                fc = FacingCooldown()
                fc_map[eid] = fc

            # 3) Si no se mueve, no actualizamos la dirección
            vx, vy = vel.vx, vel.vy
            if vx == 0 and vy == 0:
                continue

            # 4) Respetar cooldown antes de cambiar facing
            if now < fc.next_allowed:
                continue

            # 5) Determinar nueva dirección: eje mayor decide
            if abs(vx) > abs(vy):
                new_state = 'right' if vx > 0 else 'left'
            else:
                new_state = 'down' if vy > 0 else 'up'

            # 6) Actualizar solo si la dirección cambió
            if new_state != animator.current_state:
                animator.current_state = new_state
                # 7) Reiniciar cooldown (1 segundo por defecto)
                fc.next_allowed = now + 1.0
