import time
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.components.combat.burn import BurnComponent
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center


class PuddleSystem:
    """
    Actualiza charcos (puddle): aplica daño/curación periódica a entidades dentro del radio y expira.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'PuddleSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        pos_map = world.components.get('Position', {})
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})
        puddles = world.components.get('PuddleComponent', {})

        for eid, puddle in list(puddles.items()):
            # Expiración
            if now >= puddle.start_time + puddle.duration:
                puddles.pop(eid, None)
                # También limpiar Sprite/Scale si quedaron asociados
                world.components.get('Sprite', {}).pop(eid, None)
                world.components.get('Scale', {}).pop(eid, None)
                world.components.get('Position', {}).pop(eid, None)
                continue

            # Tick periódico
            if now - puddle.last_tick_time < puddle.tick_period:
                continue

            puddle.last_tick_time = now
            # Aplicar daño/curación a entidades con Health dentro del radio
            pos = pos_map.get(eid)
            if pos is None:
                continue
            r2 = float(puddle.radius) * float(puddle.radius)
            dmg = float(puddle.damage)
            heal = float(puddle.heal)
            owner = puddle.owner

            for target, thp in list(hp_map.items()):
                if target == owner:
                    continue
                if target in dead_map or target in dying_map:
                    continue
                tpos = pos_map.get(target)
                if tpos is None:
                    continue
                # Use target center vs puddle center for proper alignment
                tcx, tcy = get_entity_center(world, target)
                dx = float(tcx) - float(pos.x)
                dy = float(tcy) - float(pos.y)
                # Inflate puddle radius by an approximate entity radius so touching the edge counts
                entity_radius = 0.0
                try:
                    sprite = world.components.get('Sprite', {}).get(target)
                    scale_comp = world.components.get('Scale', {}).get(target)
                    scale = float(getattr(scale_comp, 'scale', 1.0)) if scale_comp else 1.0
                    if sprite and hasattr(sprite, 'image'):
                        w, h = sprite.image.get_size()
                        entity_radius = 0.5 * max(float(w), float(h)) * scale
                except Exception:
                    pass
                # Fallback to generic Collider size if present
                if entity_radius <= 0.0:
                    try:
                        col = world.components.get('Collider', {}).get(target)
                        if col is not None:
                            entity_radius = 0.5 * max(float(getattr(col, 'width', 0)), float(getattr(col, 'height', 0)))
                    except Exception:
                        pass
                eff_r = float(puddle.radius) + max(0.0, entity_radius)
                if dx*dx + dy*dy <= eff_r * eff_r:
                    if dmg > 0:
                        thp.current_hp = max(0, thp.current_hp - int(dmg))
                    if heal > 0:
                        thp.current_hp = min(thp.max_hp, thp.current_hp + int(heal))
                    # Apply/refresh burn when inside lava puddle
                    try:
                        if str(getattr(puddle, 'element', '')).lower() == 'lava':
                            # Defaults: 5 dmg per second for 3 seconds, 1s tick
                            dps = 5
                            dur = 3.0
                            tper = 1.0
                            st = getattr(puddle, 'status', None)
                            if isinstance(st, dict):
                                b = st.get('burn') or {}
                                if isinstance(b, dict):
                                    dps = int(b.get('dps', dps))
                                    dur = float(b.get('duration', dur))
                                    tper = float(b.get('tick_period', tper))
                            burns = world.components.setdefault('BurnComponent', {})
                            now = time.time()
                            bc = burns.get(target)
                            if bc is None:
                                burns[target] = BurnComponent(
                                    damage_per_tick=dps,
                                    duration=dur,
                                    tick_period=tper,
                                    start_time=now,
                                    last_tick_time=now,
                                    applier=owner,
                                )
                                # Immediately mark TargetHUD when burn is first applied by the player
                                try:
                                    player_id = getattr(world, 'player_entity', None)
                                    if player_id is not None and int(owner) == int(player_id):
                                        hud = world.components.setdefault('TargetHUD', {})
                                        hud['target_eid'] = int(target)
                                        hud['last_hit_time'] = float(now)
                                        if 'ttl_s' not in hud:
                                            hud['ttl_s'] = 3.0
                                except Exception:
                                    pass
                            else:
                                # Refresh burn duration while staying inside the puddle
                                bc.start_time = max(bc.start_time, now)
                                # Optionally update parameters if this puddle has stronger values
                                bc.damage_per_tick = max(bc.damage_per_tick, int(dps))
                                bc.tick_period = min(bc.tick_period, float(tper))
                                bc.duration = max(bc.duration, float(dur))
                                # Keep TargetHUD alive while player-caused burn is being refreshed
                                try:
                                    player_id = getattr(world, 'player_entity', None)
                                    if player_id is not None and int(owner) == int(player_id):
                                        hud = world.components.setdefault('TargetHUD', {})
                                        hud['target_eid'] = int(target)
                                        hud['last_hit_time'] = float(now)
                                        if 'ttl_s' not in hud:
                                            hud['ttl_s'] = 3.0
                                except Exception:
                                    pass
                    except Exception:
                        pass
