from __future__ import annotations

import time
from roguelike_engine.utils.benchmark.benchmark import benchmark


class RibbonSystem:
    """ECS system to sample positions for RibbonComponent trails.

    Responsibilities:
      - For each entity with RibbonComponent and Position, append a sampled point
        when the entity has moved more than `min_distance` since the last point.
      - Optionally prune old points by `life_time` if configured on the component.
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, camera=None):
        ribbons = world.components.get("RibbonComponent", {})
        positions = world.components.get("Position", {})
        now = time.time()
        for eid, rib in list(ribbons.items()):
            pos = positions.get(eid)
            if pos is None:
                continue
            try:
                rib.add_point(pos.x, pos.y)
            except Exception:
                # Never break the frame due to ribbon sampling
                pass
            # Extra pruning by life_time in case the component was not able to run it
            try:
                lt = getattr(rib, "life_time", None)
                if isinstance(lt, (int, float)) and lt > 0:
                    cut = now - float(lt)
                    rib.points = [p for p in rib.points if p.t_spawn >= cut]
            except Exception:
                pass
