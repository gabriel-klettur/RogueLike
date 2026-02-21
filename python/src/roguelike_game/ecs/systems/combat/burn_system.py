import time
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.combat.burn import BurnComponent
from roguelike_game.ecs.utils.health_utils import is_neutral


class BurnSystem:
    """
    Applies periodic damage to entities with BurnComponent and expires after duration.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'BurnSystem.update')
    def update(self, world, camera=None):
        burns = world.components.get('BurnComponent', {})
        if not burns:
            return
        now = time.time()
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        for eid, comp in list(burns.items()):
            # Skip dead/dying entities
            if eid in dead_map or eid in dying_map:
                continue
            # Skip burn ticks for neutral entities
            try:
                if is_neutral(world, eid):
                    continue
            except Exception:
                pass
            start = float(getattr(comp, 'start_time', 0.0))
            dur = float(getattr(comp, 'duration', 0.0))
            period = max(1e-6, float(getattr(comp, 'tick_period', 1.0)))
            end_time = start + dur
            # Determine baseline for ticking
            last = float(getattr(comp, 'last_tick_time', start))
            baseline = max(last, start)
            # Apply ticks up to min(now, end_time)
            max_tick_time = min(now, end_time)
            ticked = False
            while baseline + period <= max_tick_time + 1e-9:
                baseline += period
                hp = hp_map.get(eid)
                if hp is not None:
                    hp.current_hp = max(0, hp.current_hp - int(getattr(comp, 'damage_per_tick', 0)))
                    ticked = True
            # Update last_tick_time to the time of last applied tick
            if ticked:
                comp.last_tick_time = baseline
                # Mark target HUD if applier is the player (to show top-centered HUD on DoT)
                try:
                    player_id = getattr(world, 'player_entity', None)
                    if player_id is not None and int(getattr(comp, 'applier', -1)) == int(player_id):
                        hud = world.components.setdefault('TargetHUD', {})
                        hud['target_eid'] = int(eid)
                        hud['last_hit_time'] = float(now)
                        if 'ttl_s' not in hud:
                            hud['ttl_s'] = 3.0
                except Exception:
                    pass
            # Remove if expired (at or beyond end_time)
            if now >= end_time:
                burns.pop(eid, None)
