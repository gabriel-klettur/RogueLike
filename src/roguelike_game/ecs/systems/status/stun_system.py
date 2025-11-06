import time
from roguelike_engine.utils.benchmark import benchmark


class StunSystem:
    """
    Sistema que aplica el efecto de Parálisis (Stun):
    - Mientras dura, anula movimiento (Velocity=0) y entradas de movimiento/ataque.
    - Al expirar, elimina el componente StunComponent.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'StunSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        stuns = world.components.get('StunComponent', {})
        if not stuns:
            return
        inp_map = world.components.get('InputComponent', {})
        vel_map = world.components.get('Velocity', {})

        for eid, stun in list(stuns.items()):
            try:
                end_at = float(getattr(stun, 'start_time', now)) + float(getattr(stun, 'duration', 0.0))
            except Exception:
                end_at = now
            if now >= end_at:
                # Quitar stun expirado
                stuns.pop(eid, None)
                continue
            # Mientras dura: bloquear inputs básicos y anular velocidad
            inp = inp_map.get(eid)
            if inp is not None:
                try:
                    inp.move_x = 0
                    inp.move_y = 0
                    inp.attack = False
                except Exception:
                    pass
            vel = vel_map.get(eid)
            if vel is not None:
                try:
                    vel.vx = 0.0
                    vel.vy = 0.0
                except Exception:
                    pass
