import time
from typing import Dict, Any
from roguelike_engine.utils.benchmark.benchmark import benchmark


class DoTSystem:
    """
    Generic Damage-over-Time processor for status components like Burn and Poison.

    Responsibilities:
    - Tick periodic damage for each supported status component on living, non-neutral entities.
    - Accumulate multiple ticks if frame time skips exceed tick period.
    - Expire components when duration elapses.
    - Update HUD if the applier is the player (mirrors previous BurnSystem behavior).

    Supported components (present if world.components has the key):
    - 'BurnComponent': fields [damage_per_tick, duration, tick_period, start_time, last_tick_time, applier]
    - 'PoisonComponent': same field set as Burn
    """

    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'DoTSystem.update')
    def update(self, world: Any, camera=None) -> None:
        comps: Dict[str, Any] = world.components
        hp_map = comps.get('Health', {})
        dead_map = comps.get('DeathTimer', {})
        dying_map = comps.get('DyingTag', {})

        # Import lazily to avoid import cycles
        try:
            from roguelike_game.ecs.utils.health_utils import is_neutral  # type: ignore
        except Exception:
            def is_neutral(_world, _eid):
                return False

        now = time.time()

        # Iterate over supported DoT component maps in a fixed order for determinism
        for comp_key in ('BurnComponent', 'PoisonComponent'):
            dot_map = comps.get(comp_key, {})
            if not dot_map:
                continue

            for eid, comp in list(dot_map.items()):
                # Skip if dead/dying
                if eid in dead_map or eid in dying_map:
                    continue
                # Skip neutral entities
                try:
                    if is_neutral(world, eid):
                        continue
                except Exception:
                    pass

                # Extract timings safely
                try:
                    start = float(getattr(comp, 'start_time', now))
                    dur = float(getattr(comp, 'duration', 0.0))
                    period = max(1e-6, float(getattr(comp, 'tick_period', 1.0)))
                except Exception:
                    # If component is malformed, expire it
                    dot_map.pop(eid, None)
                    continue

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
                        try:
                            dpt = int(getattr(comp, 'damage_per_tick', 0))
                        except Exception:
                            dpt = 0
                        hp.current_hp = max(0, hp.current_hp - dpt)
                        ticked = True

                if ticked:
                    # Update last_tick_time
                    try:
                        comp.last_tick_time = baseline
                    except Exception:
                        pass
                    # HUD if applier is player
                    try:
                        player_id = getattr(world, 'player_entity', None)
                        if player_id is not None and int(getattr(comp, 'applier', -1)) == int(player_id):
                            hud = comps.setdefault('TargetHUD', {})
                            hud['target_eid'] = int(eid)
                            hud['last_hit_time'] = float(now)
                            hud.setdefault('ttl_s', 3.0)
                    except Exception:
                        pass

                # Final expiry check (if we crossed end_time precisely)
                if now >= end_time:
                    dot_map.pop(eid, None)
