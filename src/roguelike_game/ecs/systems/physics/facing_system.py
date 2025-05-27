import time
from roguelike_game.ecs.components.combat.facing_cooldown import FacingCooldown

class FacingSystem:
    """
    Sistema que actualiza el Animator.current_state basado en Velocity (4 direcciones) con cooldown.
    """
    def update(self, world):
        comps = world.components
        vel_map = comps.get('Velocity', {})
        anim_map = comps.get('Animator', {})
        fc_map = comps.get('FacingCooldown', {})
        for eid, vel in vel_map.items():
            if eid not in anim_map:
                continue
            # iniciar o obtener cooldown
            now = time.time()
            fc = fc_map.get(eid)
            if not fc:
                fc = FacingCooldown()
                fc_map[eid] = fc
            vx, vy = vel.vx, vel.vy
            # si no se mueve, no cambia facing
            if vx == 0 and vy == 0:
                continue
            # respetar cooldown
            if now < fc.next_allowed:
                continue
            # cardinal: eje mayor manda
            if abs(vx) > abs(vy):
                new_state = 'right' if vx > 0 else 'left'
            else:
                new_state = 'down' if vy > 0 else 'up'
            # actualizar solo si cambia
            if new_state != anim_map[eid].current_state:
                anim_map[eid].current_state = new_state
                fc.next_allowed = now + 1.0
