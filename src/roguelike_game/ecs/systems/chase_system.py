from ..components.position import Position
from ..components.velocity import Velocity
from ..components.movement_speed import MovementSpeed
from ..components.chase_target import ChaseTarget

class ChaseSystem:
    """
    Sistema que mueve NPCs hacia su ChaseTarget.
    """
    def update(self, world):
        comps = world.components
        # Para cada NPC con ChaseTarget
        for eid, chase in list(comps.get('ChaseTarget', {}).items()):
            target = chase.target
            # target debe tener x,y (PlayerController o entidad con Position)
            if not hasattr(target, 'x') or not hasattr(target, 'y'):
                continue
            pos = comps['Position'].get(eid)
            if not pos:
                continue
            # Obtener velocidad base
            speed_cmp = comps['MovementSpeed'].get(eid)
            speed = speed_cmp.speed if speed_cmp else 0
            # Vector hacia target
            dx = target.x - pos.x
            dy = target.y - pos.y
            # Seleccionar eje principal
            if abs(dx) > abs(dy):
                vx = speed if dx > 0 else -speed
                vy = 0
            else:
                vx = 0
                vy = speed if dy > 0 else -speed
            # Asignar Velocity
            vel = comps['Velocity'].get(eid)
            if vel:
                vel.vx = vx
                vel.vy = vy
            else:
                comps['Velocity'][eid] = Velocity(vx, vy)
