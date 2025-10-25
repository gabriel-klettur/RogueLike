import time
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.combat.burn import BurnComponent


class BurnSystem:
    """
    Applies periodic damage to entities with BurnComponent and expires after duration.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'BurnSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        burns = world.components.get('BurnComponent', {})
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        for eid, comp in list(burns.items()):
            # Expire after duration
            if now >= comp.start_time + comp.duration:
                burns.pop(eid, None)
                continue
            # Skip dead/dying
            if eid in dead_map or eid in dying_map:
                continue
            # Tick
            if now - comp.last_tick_time >= comp.tick_period:
                comp.last_tick_time = now
                hp = hp_map.get(eid)
                if hp is not None:
                    hp.current_hp = max(0, hp.current_hp - int(comp.damage_per_tick))
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
