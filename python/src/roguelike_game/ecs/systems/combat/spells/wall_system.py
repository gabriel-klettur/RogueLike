import time
from roguelike_engine.utils.benchmark.benchmark import benchmark


class WallSystem:
    """
    Gestiona la vida y expiración de segmentos de muro.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'WallSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        comps = world.components.get('WallSegmentComponent', {})
        pos_map = world.components.get('Position', {})
        to_remove = []
        for eid, wall in list(comps.items()):
            # Expirar por tiempo
            try:
                dur = float(getattr(wall, 'duration', 0.0) or 0.0)
                st = float(getattr(wall, 'start_time', 0.0) or 0.0)
                if dur > 0.0 and now >= st + dur:
                    to_remove.append(eid)
                    continue
            except Exception:
                pass
            # Desaparecer si hp <= 0
            try:
                if float(getattr(wall, 'hp', 0.0)) <= 0.0:
                    to_remove.append(eid)
                    continue
            except Exception:
                pass
        for eid in to_remove:
            comps.pop(eid, None)
            pos_map.pop(eid, None)
