import time

class AttackCooldownSystem:
    """
    Sistema que limpia componentes AttackCooldown antiguos.
    """
    def update(self, world):
        now = time.time()
        for eid, cd in list(world.components['AttackCooldown'].items()):
            # purga si pasó mucho tiempo (p.ej 60s)
            if now >= cd.next_time + 60:
                del world.components['AttackCooldown'][eid]
