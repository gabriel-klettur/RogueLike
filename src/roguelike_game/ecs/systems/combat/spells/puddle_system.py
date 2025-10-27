import time
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.components.combat.burn import BurnComponent
from roguelike_game.ecs.utils.health_utils import is_neutral


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

            # Tick periódico: primer frame aplica inmediatamente; posteriores respetan tick_period
            first_tick_pending = not bool(getattr(puddle, '_first_tick_applied', False))
            should_tick = first_tick_pending or ((now - puddle.last_tick_time) >= puddle.tick_period)
            if not should_tick:
                continue
            if first_tick_pending:
                try:
                    setattr(puddle, '_first_tick_applied', True)
                except Exception:
                    pass
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
                # Compute target collision center:
                # - Prefer MultiCollider feet center (Position + feet offsets)
                # - Else Collider AABB center (Position + offset + half size)
                # - Else fallback to Position (assumed as anchor)
                tcx, tcy = float(tpos.x), float(tpos.y)
                try:
                    mc = world.components.get('MultiCollider', {}).get(target)
                    if mc is not None:
                        colliders = getattr(mc, 'colliders', {}) or {}
                        feet = colliders.get('feet') if isinstance(colliders, dict) else None
                        if feet is not None:
                            tcx += float(getattr(feet, 'offset_x', 0.0))
                            tcy += float(getattr(feet, 'offset_y', 0.0))
                    else:
                        col = world.components.get('Collider', {}).get(target)
                        if col is not None:
                            # Only adjust by collider offsets; do not shift by size here.
                            tcx += float(getattr(col, 'offset_x', 0.0))
                            tcy += float(getattr(col, 'offset_y', 0.0))
                except Exception:
                    pass
                dx = float(tcx) - float(pos.x)
                dy = float(tcy) - float(pos.y)
                # Inflate puddle radius by an approximate entity radius so touching the edge counts
                entity_radius = 0.0
                # 1) MultiCollider feet radius if available (accurate body contact)
                try:
                    mc2 = world.components.get('MultiCollider', {}).get(target)
                    if mc2 is not None:
                        colliders2 = getattr(mc2, 'colliders', {}) or {}
                        feet2 = colliders2.get('feet') if isinstance(colliders2, dict) else None
                        if feet2 is not None and hasattr(feet2, 'radius'):
                            entity_radius = max(entity_radius, float(getattr(feet2, 'radius', 0.0)))
                except Exception:
                    pass
                # 2) Generic Collider (AABB) -> radius = half max dimension
                try:
                    col2 = world.components.get('Collider', {}).get(target)
                    if col2 is not None:
                        candidate = 0.5 * max(float(getattr(col2, 'width', 0)), float(getattr(col2, 'height', 0)))
                        entity_radius = max(entity_radius, candidate)
                except Exception:
                    pass
                # 3) Sprite-based estimate with entity Scale
                try:
                    sprite2 = world.components.get('Sprite', {}).get(target)
                    scale_comp2 = world.components.get('Scale', {}).get(target)
                    scale2 = float(getattr(scale_comp2, 'scale', 1.0)) if scale_comp2 else 1.0
                    if sprite2 and hasattr(sprite2, 'image'):
                        w2, h2 = sprite2.image.get_size()
                        candidate = 0.5 * max(float(w2), float(h2)) * scale2
                        entity_radius = max(entity_radius, candidate)
                except Exception:
                    pass
                eff_r = float(puddle.radius) + max(0.0, entity_radius)
                # Inclusive boundary check with tiny epsilon for float math stability
                if dx*dx + dy*dy <= eff_r * eff_r + 1e-6:
                    # Skip damage to neutral entities
                    if dmg > 0 and not is_neutral(world, target):
                        thp.current_hp = max(0, thp.current_hp - int(dmg))
                    if heal > 0:
                        thp.current_hp = min(thp.max_hp, thp.current_hp + int(heal))
                    # Apply/refresh burn when inside lava puddle
                    if str(getattr(puddle, 'element', '')).lower() == 'lava':
                        # Do not apply burn to neutral entities
                        if is_neutral(world, target):
                            continue
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
                                if player_id is not None and owner is not None and int(owner) == int(player_id):
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
                                if player_id is not None and owner is not None and int(owner) == int(player_id):
                                    hud = world.components.setdefault('TargetHUD', {})
                                    hud['target_eid'] = int(target)
                                    hud['last_hit_time'] = float(now)
                                    if 'ttl_s' not in hud:
                                        hud['ttl_s'] = 3.0
                            except Exception:
                                pass
