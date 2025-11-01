import time
import logging
import math
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
from roguelike_game.ecs.systems.combat.explosions_models import TimedEffectModel, FireExplosionModel
from roguelike_game.ecs.utils.health_utils import is_neutral

logger = logging.getLogger(__name__)

class MeteorFallSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'MeteorFallSystem.update')
    def update(self, world, camera=None):
        now = time.time()
        meteors = world.components.get('MeteorFallComponent', {})
        pos_map = world.components.get('Position', {})
        hp_map = world.components.get('Health', {})
        dead_map = world.components.get('DeathTimer', {})
        dying_map = world.components.get('DyingTag', {})

        for eid, m in list(meteors.items()):
            pos = pos_map.get(eid)
            if pos is None:
                meteors.pop(eid, None)
                continue
            if getattr(m, '_last_time', 0.0) <= 0.0:
                m._last_time = now
                # Asegurar que nace por encima del objetivo
                pos.y = min(pos.y, m.target_y - 1.0)
                continue
            dt = max(0.0, now - m._last_time)
            m._last_time = now

            # Mover hacia el objetivo en Y (caída vertical)
            if pos.y < m.target_y:
                pos.y = min(m.target_y, pos.y + float(m.fall_speed_px_s) * dt)

            # Impacto cuando alcanza o supera el objetivo
            if pos.y >= m.target_y - 1e-3:
                # Aplicar daño en área
                dmg = float(m.impact_damage)
                rad = float(m.impact_radius)
                r2 = rad * rad
                for target, thp in list(hp_map.items()):
                    if target in dead_map or target in dying_map:
                        continue
                    if target == m.owner:
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
                    # Incluir radio de la entidad
                    entity_radius = 0.0
                    try:
                        col2 = world.components.get('Collider', {}).get(target)
                        if col2 is not None:
                            candidate = 0.5 * max(float(getattr(col2, 'width', 0)), float(getattr(col2, 'height', 0)))
                            entity_radius = max(entity_radius, candidate)
                    except Exception:
                        pass
                    eff_r = rad + max(0.0, entity_radius)
                    dx = tcx - m.target_x
                    dy = tcy - m.target_y
                    if dx*dx + dy*dy <= eff_r * eff_r + 1e-6:
                        try:
                            thp.current_hp = max(0, thp.current_hp - int(dmg))
                        except Exception:
                            pass

                # VFX de impacto (preset si existe, fallback FireExplosionModel)
                try:
                    cfg = SPELLS.get(getattr(m, 'spell_key', ''), {})
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
                        world.components.setdefault('Position', {})[peid] = Position(m.target_x, m.target_y)
                        world.components.setdefault('ParticlePresetComponent', {})[peid] = ParticlePresetComponent(preset_id)
                        world.components.setdefault('ExplosionComponent', {})[peid] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                    else:
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
                        world.components.setdefault('Position', {})[peid2] = Position(m.target_x, m.target_y)
                        model = FireExplosionModel(
                            m.target_x,
                            m.target_y,
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

                # Eliminar entidad meteorito
                meteors.pop(eid, None)
                world.components.get('Position', {}).pop(eid, None)
                try:
                    world.remove_entity(eid)
                except Exception:
                    pass
