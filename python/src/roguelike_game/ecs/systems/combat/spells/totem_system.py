import time


class TotemSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        comps = world.components.get('TotemComponent', {})
        if not comps:
            return
        now = time.time()
        pos_map = world.components.get('Position', {})
        hp_map = world.components.get('Health', {})
        for eid, comp in list(comps.items()):
            if now >= comp.start_time + comp.duration:
                comps.pop(eid, None)
                world.components.get('Position', {}).pop(eid, None)
                continue
            last = float(getattr(comp, 'last_tick_time', 0.0) or 0.0)
            tper = max(0.05, float(getattr(comp, 'tick_period', 0.25) or 0.25))
            if last != 0.0 and (now - last) < tper:
                continue
            comp.last_tick_time = now
            pos = pos_map.get(eid)
            if pos is None:
                continue
            r2 = float(comp.radius) * float(comp.radius)
            val = float(getattr(comp, 'value', 0.0) or 0.0)
            kind = str(getattr(comp, 'kind', ''))
            for target, thp in list(hp_map.items()):
                tpos = pos_map.get(target)
                if tpos is None:
                    continue
                dx = float(tpos.x) - float(pos.x)
                dy = float(tpos.y) - float(pos.y)
                if dx*dx + dy*dy <= r2 + 1e-6:
                    if kind == 'heal' and val > 0:
                        thp.current_hp = min(thp.max_hp, thp.current_hp + val)
                    elif kind == 'damage' and val > 0:
                        thp.current_hp = max(0, thp.current_hp - val)
