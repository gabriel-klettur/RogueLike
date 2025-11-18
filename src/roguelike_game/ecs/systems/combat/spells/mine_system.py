import time
import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.systems.combat.explosions_models import TimedEffectModel, FireExplosionModel
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
from roguelike_game.ecs.utils.health_utils import is_neutral


class MineSystem:
    """
    Actualiza minas: arma tras un retardo, detecta entrada de objetivos y detona.
    - Al detonar: aplica daño en área y genera efectos de explosión (preset/partículas o modelo nativo).
    - Expira por TTL si no detona.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'MineSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        mines = world.components.get('MineComponent', {})
        pos_map = world.components.get('Position', {})
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        for eid, mine in list(mines.items()):
            # Expirar por TTL si corresponde
            if mine.ttl > 0 and (now >= mine.start_time + mine.ttl):
                # limpiar entidad y componentes asociados visuales
                mines.pop(eid, None)
                world.components.get('Sprite', {}).pop(eid, None)
                world.components.get('Scale', {}).pop(eid, None)
                world.components.get('Position', {}).pop(eid, None)
                continue

            # Aún no armada
            if now < mine.armed_at:
                continue

            # Ya detonó
            if getattr(mine, 'exploded', False):
                continue

            mpos = pos_map.get(eid)
            if mpos is None:
                continue

            # Detectar cualquier objetivo dentro de trigger_radius
            triggered = False
            tr = float(getattr(mine, 'trigger_radius', 0.0))
            tr2 = tr * tr
            for target, thp in list(hp_map.items()):
                if target in dead_map or target in dying_map:
                    continue
                if target == mine.owner:
                    continue
                # Saltar neutrales
                if is_neutral(world, target):
                    continue
                tpos = pos_map.get(target)
                if tpos is None:
                    continue
                # Centro de colisión aproximado
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
                            tcx += float(getattr(col, 'offset_x', 0.0))
                            tcy += float(getattr(col, 'offset_y', 0.0))
                except Exception:
                    pass
                dx = float(tcx) - float(mpos.x)
                dy = float(tcy) - float(mpos.y)
                # Activación por intersección: círculo overlay (tr) vs círculo entidad (entity_radius)
                entity_radius = 0.0
                try:
                    mc2 = world.components.get('MultiCollider', {}).get(target)
                    if mc2 is not None:
                        colliders2 = getattr(mc2, 'colliders', {}) or {}
                        feet2 = colliders2.get('feet') if isinstance(colliders2, dict) else None
                        if feet2 is not None and hasattr(feet2, 'radius'):
                            entity_radius = max(entity_radius, float(getattr(feet2, 'radius', 0.0)))
                except Exception:
                    pass
                try:
                    col2 = world.components.get('Collider', {}).get(target)
                    if col2 is not None:
                        candidate = 0.5 * max(float(getattr(col2, 'width', 0)), float(getattr(col2, 'height', 0)))
                        entity_radius = max(entity_radius, candidate)
                except Exception:
                    pass
                eff_tr = tr + max(0.0, entity_radius)
                if dx*dx + dy*dy <= eff_tr * eff_tr + 1e-6:
                    triggered = True
                    break

            if not triggered:
                continue

            # Detonación: aplicar daño en área y spawn VFX
            mine.exploded = True
            payload = getattr(mine, 'payload', {}) or {}
            exp = payload.get('explosion', {}) if isinstance(payload, dict) else {}
            damage = float(exp.get('damage', 0))
            radius = float(exp.get('radius', 120))
            r2 = radius * radius

            # Daño en área
            for target, thp in list(hp_map.items()):
                if target in dead_map or target in dying_map:
                    continue
                if target == mine.owner:
                    continue
                if is_neutral(world, target):
                    continue
                tpos = pos_map.get(target)
                if tpos is None:
                    continue
                # Centro aproximado
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
                            tcx += float(getattr(col, 'offset_x', 0.0))
                            tcy += float(getattr(col, 'offset_y', 0.0))
                except Exception:
                    pass
                dx = float(tcx) - float(mpos.x)
                dy = float(tcy) - float(mpos.y)
                # Incluir radio de entidad
                entity_radius = 0.0
                try:
                    col2 = world.components.get('Collider', {}).get(target)
                    if col2 is not None:
                        candidate = 0.5 * max(float(getattr(col2, 'width', 0)), float(getattr(col2, 'height', 0)))
                        entity_radius = max(entity_radius, candidate)
                except Exception:
                    pass
                eff_r = radius + max(0.0, entity_radius)
                if dx*dx + dy*dy <= eff_r * eff_r + 1e-6:
                    thp.current_hp = max(0, thp.current_hp - int(damage))

            try:
                blds = getattr(world, 'buildings', []) or []
                if blds and damage > 0:
                    left = float(mpos.x) - float(radius)
                    top = float(mpos.y) - float(radius)
                    diam = int(float(radius) * 2)
                    surf = pygame.Surface((diam, diam), pygame.SRCALPHA)
                    pygame.draw.circle(surf, (255, 255, 255), (diam // 2, diam // 2), diam // 2)
                    cmask = pygame.mask.from_surface(surf)
                    circle_rect = pygame.Rect(int(left), int(top), int(diam), int(diam))
                    for b in blds:
                        if getattr(b, 'runtime_hidden', False):
                            continue
                        if not bool(getattr(b, '_is_spawner_visual', False)):
                            continue
                        eff = getattr(b, '_spawner_visual_life_cfg', None) or {}
                        if not bool(eff.get('damageable', False)):
                            continue
                        quick_rect = getattr(b, 'rect', None)
                        if quick_rect and not circle_rect.colliderect(quick_rect):
                            continue
                        bm = getattr(b, 'model', None)
                        bmask = bm.get_full_mask() if bm is not None else None
                        if bmask is not None:
                            off = (int(b.x - left), int(b.y - top))
                            if cmask.overlap(bmask, off):
                                se = getattr(b, '_spawner_eid', None)
                                if se is not None:
                                    world.components.setdefault('SpawnerDamageEvents', []).append({
                                        'spawner_eid': int(se),
                                        'damage': float(damage),
                                        'attacker': int(getattr(mine, 'owner', 0)) if getattr(mine, 'owner', None) is not None else None,
                                        'source': 'mine'
                                    })
                                continue
                        for rect_w in getattr(b, 'collision_tiles', []) or []:
                            if not circle_rect.colliderect(rect_w):
                                continue
                            se = getattr(b, '_spawner_eid', None)
                            if se is not None:
                                world.components.setdefault('SpawnerDamageEvents', []).append({
                                    'spawner_eid': int(se),
                                    'damage': float(damage),
                                    'attacker': int(getattr(mine, 'owner', 0)) if getattr(mine, 'owner', None) is not None else None,
                                    'source': 'mine'
                                })
                                break
            except Exception:
                pass

            # Spawn VFX explosion (preset si está definido en spells.cfg[vfx.impact], si no FireExplosionModel)
            try:
                cfg = SPELLS.get(getattr(mine, 'spell_key', ''), {})
                vfx_obj = None
                try:
                    vfx_attr = getattr(cfg, 'vfx', None)
                    if isinstance(vfx_attr, dict):
                        vfx_obj = vfx_attr
                    else:
                        vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                except Exception:
                    vfx_obj = None
                preset_id = None
                ttl_ticks = None
                if isinstance(vfx_obj, dict):
                    impact = vfx_obj.get('impact') or {}
                    if isinstance(impact, dict):
                        if isinstance(impact.get('preset'), str):
                            preset_id = impact.get('preset')
                        if isinstance(impact.get('ttl'), (int, float)):
                            ttl_ticks = int(impact.get('ttl'))
                        exp_cfg = impact.get('explosion') or {}
                        if isinstance(exp_cfg, dict):
                            if isinstance(exp_cfg.get('preset'), str):
                                preset_id = exp_cfg.get('preset')
                            if isinstance(exp_cfg.get('ttl'), (int, float)):
                                ttl_ticks = int(exp_cfg.get('ttl'))
                if isinstance(preset_id, str) and preset_id:
                    peid = world.create_entity()
                    world.components.setdefault('Position', {})[peid] = Position(mpos.x, mpos.y)
                    world.components.setdefault('ParticlePresetComponent', {})[peid] = ParticlePresetComponent(preset_id)
                    world.components.setdefault('ExplosionComponent', {})[peid] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                else:
                    # Fallback: FireExplosionModel con parámetros desde vfx.impact.explosion si existen
                    pcount = 100
                    pscale = 1.0
                    colors = None
                    gravity = None
                    drag = None
                    blend_mode = None
                    sol = None
                    aol = None
                    col_ol = None
                    try:
                        exp_cfg2 = None
                        if isinstance(vfx_obj, dict):
                            impact2 = vfx_obj.get('impact') or {}
                            if isinstance(impact2, dict):
                                exp_cfg2 = impact2.get('explosion')
                        if isinstance(exp_cfg2, dict):
                            if isinstance(exp_cfg2.get('particle_count'), int):
                                pcount = int(exp_cfg2.get('particle_count'))
                            if isinstance(exp_cfg2.get('scale'), (int, float)):
                                pscale = float(exp_cfg2.get('scale'))
                            cols = exp_cfg2.get('colors')
                            if isinstance(cols, (list, tuple)) and len(cols) > 0:
                                tmp = []
                                for c in cols:
                                    if isinstance(c, (list, tuple)) and len(c) >= 3:
                                        tmp.append((int(c[0]), int(c[1]), int(c[2])))
                                colors = tmp if tmp else None
                            gv = exp_cfg2.get('gravity')
                            if isinstance(gv, (int, float)):
                                gravity = (0.0, float(gv))
                            elif isinstance(gv, (list, tuple)) and len(gv) >= 2:
                                gravity = (float(gv[0]), float(gv[1]))
                            dg = exp_cfg2.get('drag')
                            if isinstance(dg, (int, float)):
                                drag = float(dg)
                            if isinstance(exp_cfg2.get('blend_mode'), str):
                                blend_mode = exp_cfg2.get('blend_mode')
                            sol = exp_cfg2.get('size_over_life') if isinstance(exp_cfg2.get('size_over_life'), (list, tuple)) else None
                            aol = exp_cfg2.get('alpha_over_life') if isinstance(exp_cfg2.get('alpha_over_life'), (list, tuple)) else None
                            col_ol = exp_cfg2.get('color_over_life') if isinstance(exp_cfg2.get('color_over_life'), (list, tuple)) else None
                    except Exception:
                        pass
                    peid2 = world.create_entity()
                    world.components.setdefault('Position', {})[peid2] = Position(mpos.x, mpos.y)
                    model = FireExplosionModel(
                        mpos.x,
                        mpos.y,
                        particle_count=pcount,
                        scale=pscale,
                        colors=colors,
                        gravity=gravity,
                        drag=drag,
                        blend_mode=blend_mode,
                        size_over_life=sol,
                        alpha_over_life=aol,
                        color_over_life=col_ol,
                    )
                    world.components.setdefault('ExplosionComponent', {})[peid2] = ExplosionComponent(model)
            except Exception:
                pass

            # Remover entidad de la mina una vez detonada
            mines.pop(eid, None)
            world.components.get('Sprite', {}).pop(eid, None)
            world.components.get('Scale', {}).pop(eid, None)
            world.components.get('Position', {}).pop(eid, None)
