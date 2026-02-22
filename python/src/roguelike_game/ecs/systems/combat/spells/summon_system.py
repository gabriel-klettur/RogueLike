import time


class SummonSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        comps = world.components.get('SummonedUnitComponent', {})
        if not comps:
            return
        now = time.time()
        for eid, comp in list(comps.items()):
            if now >= comp.start_time + comp.duration:
                comps.pop(eid, None)
                try:
                    world.remove_entity(eid)
                except Exception:
                    pass
