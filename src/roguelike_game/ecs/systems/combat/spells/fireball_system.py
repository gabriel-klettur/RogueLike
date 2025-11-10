import pygame
from roguelike_engine.utils.benchmark import benchmark
import math
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.explosion_component import ExplosionComponent
from roguelike_game.ecs.systems.combat.explosions_models import TimedEffectModel, FireExplosionModel
from roguelike_game.ecs.components.particles.particle_preset_component import ParticlePresetComponent
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
import time
from roguelike_game.ecs.utils.collider_utils import circle_overlaps_obb
from roguelike_game.ecs.components.combat.last_attacker import LastAttacker
from roguelike_game.ecs.utils.health_utils import is_neutral
import logging
logger = logging.getLogger(__name__)

class FireballSystem:
    """
    Sistema que actualiza fireballs: movimiento, edad, colisiones con NPC y tiles.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
        # Cache de máscaras circulares por radio entero para pruebas rápidas de colisión máscara<->círculo
        self._circle_masks: dict[int, pygame.mask.Mask] = {}
    
    def update(self, world, camera=None):
        # Actualizar cada fireball
        fbd = world.components.get('FireballComponent', {})
        if not getattr(self, '_dbg_logged_count', False):
            setattr(self, '_dbg_logged_count', True)
            try:
                logger.debug("[FireballSystem] start update: fireballs=%d", len(fbd))
            except Exception:
                pass
        # Precompute walls (wall segments) data once per update to avoid O(F·W)
        walls_data_cache = []
        try:
            wmap_all = world.components.get('WallSegmentComponent', {})
            if wmap_all:
                pmap_all = world.components.get('Position', {})
                for wid, w in list(wmap_all.items()):
                    try:
                        if not bool(getattr(w, 'blocks_projectiles', True)):
                            continue
                        wpos = pmap_all.get(wid)
                        if wpos is None:
                            continue
                        half_w = float(getattr(w, 'half_w', getattr(w, 'width', 0.0) * 0.5) or 0.0)
                        half_h = float(getattr(w, 'half_h', getattr(w, 'height', 0.0) * 0.5) or 0.0)
                        cos_a = float(getattr(w, 'cos_a', 1.0))
                        sin_a = float(getattr(w, 'sin_a', 0.0))
                        ext_x = abs(cos_a) * half_w + abs(sin_a) * half_h
                        ext_y = abs(sin_a) * half_w + abs(cos_a) * half_h
                        aabb = pygame.Rect(int(wpos.x - ext_x), int(wpos.y - ext_y), int(ext_x * 2), int(ext_y * 2))
                        walls_data_cache.append({
                            'wx': float(wpos.x), 'wy': float(wpos.y),
                            'half_w': half_w, 'half_h': half_h,
                            'cos': cos_a, 'sin': sin_a,
                            'aabb': aabb,
                        })
                    except Exception:
                        continue
        except Exception:
            walls_data_cache = []
        for eid in list(fbd):
            comp = world.components['FireballComponent'][eid]
            pos = world.components['Position'][eid]
            vel = world.components['Velocity'][eid]
            # Movimiento
            pos.x += vel.vx
            pos.y += vel.vy
            comp.age += 1
            # Lifespan: destruir si supera su vida útil configurada (>0)
            try:
                if getattr(comp, 'lifespan', 0) and comp.age >= int(getattr(comp, 'lifespan', 0)):
                    world.remove_entity(eid)
                    continue
            except Exception:
                pass
            # Radio efectivo de colisión (escala con meta)
            try:
                hit_radius = float(getattr(comp, 'hit_radius', 2.0))
            except Exception:
                hit_radius = 2.0
            # Trayectoria del frame para evitar tunneling
            prev_x = pos.x - vel.vx
            prev_y = pos.y - vel.vy
            dx = pos.x - prev_x
            dy = pos.y - prev_y
            dist = (dx*dx + dy*dy) ** 0.5
            # Paso de muestreo más fino para radios pequeños; evita tunneling con proyectiles rápidos
            step = max(1.0, float(hit_radius) * 0.5)
            # Limitar número máximo de muestras para reducir coste O(N*M), pero con mayor tope
            samples = max(1, int(dist / step))
            if samples > 12:
                samples = 12
            if samples <= 1:
                sample_points = [(pos.x, pos.y)]
            else:
                sample_points = []
                for i in range(samples + 1):
                    t = i / samples
                    sx = prev_x + dx * t
                    sy = prev_y + dy * t
                    sample_points.append((sx, sy))
            # Broad-phase: AABB de la trayectoria expandida por el radio
            left   = int(min(prev_x, pos.x) - hit_radius)
            top    = int(min(prev_y, pos.y) - hit_radius)
            right  = int(max(prev_x, pos.x) + hit_radius)
            bottom = int(max(prev_y, pos.y) + hit_radius)
            path_aabb = pygame.Rect(left, top, max(1, right - left + 1), max(1, bottom - top + 1))
            # Destruir si supera rango configurado
            cfg = SPELLS.get(getattr(comp, 'spell_key', ''), {})
            max_range = cfg.get('range', 0)
            if max_range and comp.spawn_pos:
                dxr = pos.x - comp.spawn_pos[0]
                dyr = pos.y - comp.spawn_pos[1]
                if math.hypot(dxr, dyr) > max_range:
                    try:
                        logger.debug("[FireballSystem] remove eid=%s by range (%.1f > %.1f)", eid, math.hypot(dxr, dyr), max_range)
                    except Exception:
                        pass
                    world.remove_entity(eid)
                    continue
            # Colisión con visuals activos de Spawner (Buildings): broad-phase con AABB de trayectoria
            try:
                # Evitar colisiones con edificios ocultos o que no sean visuals de spawner
                hit_spawner_eid = None
                # sample_points precomputados arriba
                for b in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(b, 'runtime_hidden', False):
                            continue
                        if not bool(getattr(b, '_is_spawner_visual', False)):
                            continue
                        # Daño permitido sólo si el visual actual es damageable
                        eff = getattr(b, '_spawner_visual_life_cfg', None) or {}
                        if not bool(eff.get('damageable', False)):
                            continue
                        # Prueba principal: shape del asset (alpha mask de la imagen completa)
                        bm = getattr(b, 'model', None)
                        bmask = bm.get_full_mask() if bm is not None else None
                        if bmask is not None and getattr(bm, 'image', None) is not None:
                            iw, ih = bm.image.get_size()
                            # AABB del asset; filtrar por trayectoria
                            b_full_rect = pygame.Rect(int(b.x), int(b.y), int(iw), int(ih))
                            if not path_aabb.colliderect(b_full_rect):
                                continue
                            hit = False
                            # Preparar máscara circular
                            r = max(1, int(round(hit_radius)))
                            cmask = self._circle_masks.get(r)
                            if cmask is None:
                                surf = pygame.Surface((2*r+1, 2*r+1), pygame.SRCALPHA)
                                pygame.draw.circle(surf, (255, 255, 255, 255), (r, r), r)
                                cmask = pygame.mask.from_surface(surf)
                                self._circle_masks[r] = cmask
                            for (sx, sy) in sample_points:
                                lx = int(round(sx - b.x))
                                ly = int(round(sy - b.y))
                                # Offset del círculo respecto a la máscara del edificio
                                offx = lx - r
                                offy = ly - r
                                if bmask.overlap(cmask, (offx, offy)) is not None:
                                    hit = True
                                    break
                            if not hit:
                                continue
                        else:
                            # Fallback: bounding rect + tiles
                            rect = getattr(b, 'rect', None) or b.collision_rect
                            # Filtrado broad-phase con path_aabb
                            if rect and (not path_aabb.colliderect(rect)):
                                continue
                            # Aproximar círculo por un rectángulo envolvente y probar intersección
                            rect_hit = False
                            for (sx, sy) in sample_points:
                                circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                                if rect.colliderect(circle_rect):
                                    rect_hit = True
                                    break
                            if not rect_hit:
                                continue
                            tiles = list(getattr(b, 'collision_tiles', []) or [])
                            if tiles:
                                matched = False
                                for r in tiles:
                                    for (sx, sy) in sample_points:
                                        circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                                        if r.colliderect(circle_rect):
                                            matched = True
                                            break
                                    if matched:
                                        break
                                if not matched:
                                    continue
                        # Registrar impacto
                        se = getattr(b, '_spawner_eid', None)
                        if se is not None:
                            hit_spawner_eid = int(se)
                            break
                    except Exception:
                        continue
                if hit_spawner_eid is not None:
                    # Publicar evento de daño de spawner y generar explosión como feedback
                    sevts = world.components.setdefault('SpawnerDamageEvents', [])
                    sevts.append({'spawner_eid': int(hit_spawner_eid), 'damage': float(comp.damage), 'attacker': int(comp.caster) if comp.caster is not None else None})
                    # Spawn ECS explosion at collision point
                    try:
                        x, y = pos.x, pos.y
                        eid2 = world.create_entity()
                        world.components['Position'][eid2] = Position(x, y)
                        # Gather optional advanced explosion params from cfg
                        exp_cfg = None
                        try:
                            vfx_obj = None
                            vfx_attr = getattr(cfg, 'vfx', None)
                            if isinstance(vfx_attr, dict):
                                vfx_obj = vfx_attr
                            else:
                                vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                            impact = vfx_obj.get('impact') if isinstance(vfx_obj, dict) else None
                            exp_cfg = impact.get('explosion') if isinstance(impact, dict) else None
                        except Exception:
                            exp_cfg = None
                        # Defaults
                        pcount = None
                        pscale = None
                        colors = None
                        gravity = None
                        drag = None
                        blend_mode = None
                        sol = None
                        aol = None
                        col_ol = None
                        if isinstance(exp_cfg, dict):
                            # particle count/scale (optional)
                            if isinstance(exp_cfg.get('particle_count'), int):
                                pcount = int(exp_cfg.get('particle_count'))
                            if isinstance(exp_cfg.get('scale'), (int, float)):
                                pscale = float(exp_cfg.get('scale'))
                            # colors palette
                            cols = exp_cfg.get('colors')
                            if isinstance(cols, (list, tuple)) and len(cols) > 0:
                                tmp = []
                                for c in cols:
                                    if isinstance(c, (list, tuple)) and len(c) >= 3:
                                        tmp.append((int(c[0]), int(c[1]), int(c[2])))
                                colors = tmp if tmp else None
                            # forces
                            gv = exp_cfg.get('gravity')
                            if isinstance(gv, (int, float)):
                                gravity = (0.0, float(gv))
                            elif isinstance(gv, (list, tuple)) and len(gv) >= 2:
                                gravity = (float(gv[0]), float(gv[1]))
                            dg = exp_cfg.get('drag')
                            if isinstance(dg, (int, float)):
                                drag = float(dg)
                            # blend mode
                            if isinstance(exp_cfg.get('blend_mode'), str):
                                blend_mode = exp_cfg.get('blend_mode')
                            # curves
                            sol = exp_cfg.get('size_over_life') if isinstance(exp_cfg.get('size_over_life'), (list, tuple)) else None
                            aol = exp_cfg.get('alpha_over_life') if isinstance(exp_cfg.get('alpha_over_life'), (list, tuple)) else None
                            col_ol = exp_cfg.get('color_over_life') if isinstance(exp_cfg.get('color_over_life'), (list, tuple)) else None
                        # Instantiate explosion model with advanced params (fallbacks inside model/Particle)
                        model = FireExplosionModel(
                            x,
                            y,
                            particle_count=pcount if isinstance(pcount, int) and pcount > 0 else 100,
                            scale=pscale if isinstance(pscale, (int, float)) and pscale > 0 else 1.0,
                            colors=colors,
                            gravity=gravity,
                            drag=drag,
                            blend_mode=blend_mode,
                            size_over_life=sol,
                            alpha_over_life=aol,
                            color_over_life=col_ol,
                        )
                        world.components['ExplosionComponent'][eid2] = ExplosionComponent(model)
                    except Exception:
                        pass
                    world.remove_entity(eid)
                    continue
            except Exception:
                # No romper la lógica de fireball si hay fallo al procesar buildings
                pass
            # Colisión con muros dinámicos OBB (wall segments) que bloquean proyectiles
            try:
                if walls_data_cache:
                    # Pre-filtrado por AABB de trayectoria para reducir número de muros candidatos
                    candidate_walls = [w for w in walls_data_cache if path_aabb.colliderect(w['aabb'])]
                    # Test sampled trajectory against walls
                    hit = False
                    hit_point = None
                    for (sx, sy) in sample_points:
                        # Broad-phase: projectile circle AABB vs wall AABB
                        c_aabb = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                        for w in candidate_walls:
                            if not c_aabb.colliderect(w['aabb']):
                                continue
                            if circle_overlaps_obb(sx, sy, hit_radius, w['wx'], w['wy'], w['half_w'], w['half_h'], w['cos'], w['sin']):
                                hit = True
                                hit_point = (float(sx), float(sy))
                                break
                        if hit:
                            break
                    if hit:
                        # Spawn impact VFX like tile collision (preset-based if defined)
                        try:
                            preset_id = None
                            ttl_ticks = None
                            vfx_obj = None
                            try:
                                vfx_attr = getattr(cfg, 'vfx', None)
                                if isinstance(vfx_attr, dict):
                                    vfx_obj = vfx_attr
                                else:
                                    vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                            except Exception:
                                vfx_obj = None
                            if isinstance(vfx_obj, dict):
                                impact = vfx_obj.get('impact') or {}
                                if isinstance(impact, dict):
                                    if isinstance(impact.get('preset'), str):
                                        preset_id = impact.get('preset')
                                    if isinstance(impact.get('ttl'), (int, float)):
                                        ttl_ticks = int(impact.get('ttl'))
                                    exp = impact.get('explosion') or {}
                                    if isinstance(exp, dict):
                                        if isinstance(exp.get('preset'), str):
                                            preset_id = exp.get('preset')
                                        if isinstance(exp.get('ttl'), (int, float)):
                                            ttl_ticks = int(exp.get('ttl'))
                            if isinstance(preset_id, str) and preset_id:
                                x, y = hit_point if hit_point else (pos.x, pos.y)
                                eid2 = world.create_entity()
                                world.components.setdefault('Position', {})[eid2] = Position(x, y)
                                # Escalar impacto según multiplicador del proyectil
                                try:
                                    smul = float(getattr(comp, 'vfx_scale_multiplier', 1.0))
                                except Exception:
                                    smul = 1.0
                                world.components.setdefault('ParticlePresetComponent', {})[eid2] = ParticlePresetComponent(preset_id, scale_multiplier=smul)
                                world.components.setdefault('ExplosionComponent', {})[eid2] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                        except Exception:
                            pass
                        world.remove_entity(eid)
                        continue
            except Exception:
                pass

            # Colisión con NPCs (usar MaskCollider pixel-perfect siempre que exista)
            for target in world.get_entities_with('Position', 'MultiCollider', 'Health'):
                # Saltar self, caster y cadáveres con DeathTimer
                if target == eid or target == comp.caster:
                    continue
                if target in world.components.get('DeathTimer', {}) or target in world.components.get('DyingTag', {}):
                    continue
                multi = world.components['MultiCollider'][target]
                tpos = world.components['Position'][target]
                # Broad-phase: usar AABB unión de todos los colliders del target (no solo 'feet')
                rects = []
                try:
                    for col in multi.colliders.values():
                        if hasattr(col, 'radius'):
                            cx = int(tpos.x + getattr(col, 'offset_x', 0))
                            cy = int(tpos.y + getattr(col, 'offset_y', 0))
                            cr = int(getattr(col, 'radius', 0))
                            rects.append(pygame.Rect(cx - cr, cy - cr, cr * 2 + 1, cr * 2 + 1))
                        elif hasattr(col, 'mask'):
                            ax = int(tpos.x + getattr(col, 'offset_x', 0))
                            ay = int(tpos.y + getattr(col, 'offset_y', 0))
                            try:
                                mw, mh = col.mask.get_size()
                            except Exception:
                                mw, mh = 0, 0
                            rects.append(pygame.Rect(ax, ay, int(mw), int(mh)))
                        else:
                            ax = int(tpos.x + getattr(col, 'offset_x', 0))
                            ay = int(tpos.y + getattr(col, 'offset_y', 0))
                            aw = int(getattr(col, 'width', 0))
                            ah = int(getattr(col, 'height', 0))
                            rects.append(pygame.Rect(ax, ay, aw, ah))
                except Exception:
                    rects = []
                if rects:
                    minx = min(r.left for r in rects)
                    miny = min(r.top for r in rects)
                    maxr = max(r.right for r in rects)
                    maxb = max(r.bottom for r in rects)
                    aabb = pygame.Rect(minx, miny, max(1, maxr - minx), max(1, maxb - miny))
                    aabb.inflate_ip(int(2*hit_radius)+1, int(2*hit_radius)+1)
                    if not path_aabb.colliderect(aabb):
                        continue
                hit = False
                hit_pos = None
                hit_shape = None
                # Determinar si el target tiene al menos un MaskCollider
                has_mask = any(hasattr(c, 'mask') for c in multi.colliders.values())
                # 1) Intentar con máscaras (pixel-perfect) si existen (usar círculo y sweep por sample_points)
                if has_mask:
                    for col in multi.colliders.values():
                        if hasattr(col, 'mask'):
                            bx = tpos.x + col.offset_x
                            by = tpos.y + col.offset_y
                            # Preparar máscara circular
                            r = max(1, int(round(hit_radius)))
                            cmask = self._circle_masks.get(r)
                            if cmask is None:
                                surf = pygame.Surface((2*r+1, 2*r+1), pygame.SRCALPHA)
                                pygame.draw.circle(surf, (255, 255, 255, 255), (r, r), r)
                                cmask = pygame.mask.from_surface(surf)
                                self._circle_masks[r] = cmask
                            for (sx, sy) in sample_points:
                                lx = int(round(sx - bx))
                                ly = int(round(sy - by))
                                # Offset del círculo respecto a la máscara del edificio
                                offx = lx - r
                                offy = ly - r
                                if col.mask.overlap(cmask, (offx, offy)) is not None:
                                    hit = True
                                    hit_pos = (float(sx), float(sy))
                                    hit_shape = 'mask'
                                    break
                            if hit:
                                break
                # 2) Si no hubo hit con máscaras, probar círculos (feet) si existen
                if not hit:
                    for col in multi.colliders.values():
                        if hasattr(col, 'radius'):
                            cx = float(tpos.x + getattr(col, 'offset_x', 0))
                            cy = float(tpos.y + getattr(col, 'offset_y', 0))
                            cr = float(getattr(col, 'radius', 0))
                            if cr <= 0:
                                continue
                            # Probar a lo largo de la trayectoria
                            for (sx, sy) in sample_points:
                                dx2 = sx - cx
                                dy2 = sy - cy
                                # Colisión de dos círculos: distancia <= suma de radios
                                if (dx2*dx2 + dy2*dy2) <= (hit_radius + cr) * (hit_radius + cr):
                                    hit = True
                                    hit_pos = (float(sx), float(sy))
                                    hit_shape = 'circle'
                                    break
                            if hit:
                                break
                # 3) Solo si NO hay máscaras ni círculos o no hubo hit, usar fallback a rectángulos
                if not hit and not has_mask:
                    for col in multi.colliders.values():
                        if hasattr(col, 'mask'):
                            continue
                        rect = pygame.Rect(
                            tpos.x + col.offset_x,
                            tpos.y + col.offset_y,
                            getattr(col, 'width', 0),
                            getattr(col, 'height', 0)
                        )
                        for (sx, sy) in sample_points:
                            circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                            if rect.colliderect(circle_rect):
                                hit = True
                                hit_pos = (float(sx), float(sy))
                                hit_shape = 'rect'
                                break
                        if hit:
                            break

                if hit:
                    # Inmunidad de neutrales: saltar daños/efectos
                    if is_neutral(world, target):
                        world.remove_entity(eid)
                        break
                    # Spawn preset-based explosion VFX at impact point (only if preset explicitly configured)
                    try:
                        preset_id = None
                        ttl_ticks = None
                        if cfg is None:
                            cfg = SPELLS.get(getattr(comp, 'spell_key', ''), {})
                        vfx_obj = None
                        try:
                            vfx_attr = getattr(cfg, 'vfx', None)
                            if isinstance(vfx_attr, dict):
                                vfx_obj = vfx_attr
                            else:
                                vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                        except Exception:
                            vfx_obj = None
                        if isinstance(vfx_obj, dict):
                            impact = vfx_obj.get('impact') or {}
                            if isinstance(impact, dict):
                                if isinstance(impact.get('preset'), str):
                                    preset_id = impact.get('preset')
                                if isinstance(impact.get('ttl'), (int, float)):
                                    ttl_ticks = int(impact.get('ttl'))
                                exp = impact.get('explosion') or {}
                                if isinstance(exp, dict):
                                    if isinstance(exp.get('preset'), str):
                                        preset_id = exp.get('preset')
                                    if isinstance(exp.get('ttl'), (int, float)):
                                        ttl_ticks = int(exp.get('ttl'))
                        if isinstance(preset_id, str) and preset_id:
                            x, y = hit_pos if hit_pos else (pos.x, pos.y)
                            peid = world.create_entity()
                            world.components.setdefault('Position', {})[peid] = Position(x, y)
                            # Escalar impacto según multiplicador del proyectil
                            try:
                                smul = float(getattr(comp, 'vfx_scale_multiplier', 1.0))
                            except Exception:
                                smul = 1.0
                            world.components.setdefault('ParticlePresetComponent', {})[peid] = ParticlePresetComponent(preset_id, scale_multiplier=smul)
                            world.components.setdefault('ExplosionComponent', {})[peid] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                        else:
                            # Fallback: if explosion config present but no preset, spawn native FireExplosionModel with advanced params
                            try:
                                exp_cfg2 = None
                                if isinstance(vfx_obj, dict):
                                    impact2 = vfx_obj.get('impact') or {}
                                    if isinstance(impact2, dict):
                                        exp_cfg2 = impact2.get('explosion')
                                if isinstance(exp_cfg2, dict):
                                    # Extract advanced params
                                    pcount = int(exp_cfg2.get('particle_count')) if isinstance(exp_cfg2.get('particle_count'), int) else 100
                                    pscale = float(exp_cfg2.get('scale')) if isinstance(exp_cfg2.get('scale'), (int, float)) else 1.0
                                    colors = None
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
                                    else:
                                        gravity = None
                                    drag = float(exp_cfg2.get('drag')) if isinstance(exp_cfg2.get('drag'), (int, float)) else None
                                    blend_mode = exp_cfg2.get('blend_mode') if isinstance(exp_cfg2.get('blend_mode'), str) else None
                                    sol = exp_cfg2.get('size_over_life') if isinstance(exp_cfg2.get('size_over_life'), (list, tuple)) else None
                                    aol = exp_cfg2.get('alpha_over_life') if isinstance(exp_cfg2.get('alpha_over_life'), (list, tuple)) else None
                                    col_ol = exp_cfg2.get('color_over_life') if isinstance(exp_cfg2.get('color_over_life'), (list, tuple)) else None
                                    x2, y2 = hit_pos if hit_pos else (pos.x, pos.y)
                                    peid2 = world.create_entity()
                                    world.components.setdefault('Position', {})[peid2] = Position(x2, y2)
                                    model2 = FireExplosionModel(
                                        x2,
                                        y2,
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
                                    world.components.setdefault('ExplosionComponent', {})[peid2] = ExplosionComponent(model2)
                            except Exception:
                                pass
                    except Exception:
                        pass
                    # Inmortalidad del jugador en godmode
                    is_player = target in world.components.get('PlayerTagComponent', {})
                    godmode = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and is_player
                    # One-shot si el caster es jugador y godmode activo
                    gm_attacker = bool(getattr(getattr(world, 'state', None), 'godmode', False)) and (comp.caster in world.components.get('PlayerTagComponent', {}))
                    if not godmode:
                        hp = world.components['Health'][target]
                        if gm_attacker:
                            hp.current_hp = 0
                        else:
                            hp.current_hp = max(0, hp.current_hp - comp.damage)
                    # Registrar último atacante para atribuir KO si entra en UnconsciousState (solo si aplica daño)
                    if not godmode:
                        world.components.setdefault('LastAttacker', {})[target] = LastAttacker(comp.caster, time.time())
                    # Push debug event for outline persistence (consumed by SpellCollisionDebugSystem)
                    dbg = world.components.setdefault('DebugSpellHits', {})
                    queue = dbg.setdefault('_queue', [])
                    queue.append({'type': 'FB', 'src': eid, 'target': target, 'pos': hit_pos, 'shape': hit_shape})
                    world.remove_entity(eid)
                    # Publicar eventos FSM para NPCs golpeados por jugador o jugador golpeado por NPC
                    caster = comp.caster
                    if caster in world.components.get('PlayerTagComponent', {}):
                        # Jugador -> NPC
                        attacker_pos = world.components['Position'][caster]
                        defender_pos = world.components['Position'][target]
                        from_left = attacker_pos.x < defender_pos.x
                        qmap = world.components.setdefault('FSMEventQueue', {})
                        q = qmap.setdefault(target, [])
                        q.append({"type": "OnHit", "from_left": from_left})
                        if not godmode:
                            hp = world.components['Health'][target]
                            if hp.current_hp <= 0:
                                pass
                            # Evento de COMBO
                            combo_q = world.components.setdefault('ComboEventQueue', [])
                            combo_q.append({
                                'attacker': caster,
                                'target': target,
                                'damage': float(comp.damage),
                                'source': 'fireball',
                                'time': float(time.time()),
                            })
                        # Actualizar HUD de objetivo (centrado arriba)
                        try:
                            hud = world.components.setdefault('TargetHUD', {})
                            hud['target_eid'] = int(target)
                            hud['last_hit_time'] = float(time.time())
                            hud.setdefault('ttl_s', 3.0)
                        except Exception:
                            pass

                    elif is_player:
                        # NPC -> Jugador (omitir efectos de daño en godmode)
                        if not godmode:
                            attacker_pos = world.components['Position'].get(caster)
                            defender_pos = world.components['Position'].get(target)
                            if attacker_pos and defender_pos:
                                from_left = attacker_pos.x < defender_pos.x
                            else:
                                from_left = False
                            qmap = world.components.setdefault('FSMEventQueue', {})
                            q = qmap.setdefault(target, [])
                            q.append({"type": "OnHit", "from_left": from_left})
                            hp = world.components['Health'][target]
                            if hp.current_hp <= 0:
                                pass
                            # Romper combo del jugador al recibir daño
                            combo_q = world.components.setdefault('ComboEventQueue', [])

                            combo_q.append({'type': 'break', 'entity': target})
                    break

            # Colisión con tiles sólidos
            # Consultar tiles una única vez para toda la trayectoria (broad-phase), luego probar muestras
            collided = False
            hit_point_tile = None
            try:
                nearby_tiles = world.get_solid_tiles_for_rect(path_aabb)
            except Exception:
                nearby_tiles = None
            for (sx, sy) in sample_points:
                circle_rect = pygame.Rect(int(sx - hit_radius), int(sy - hit_radius), int(2*hit_radius)+1, int(2*hit_radius)+1)
                if nearby_tiles and any(r.colliderect(circle_rect) for r in nearby_tiles):
                    collided = True
                    hit_point_tile = (float(sx), float(sy))
                    break
            if collided:
                # Spawn preset-based explosion at collision point (only if preset explicitly configured)
                try:
                    preset_id = None
                    ttl_ticks = None
                    vfx_obj = None
                    try:
                        vfx_attr = getattr(cfg, 'vfx', None)
                        if isinstance(vfx_attr, dict):
                            vfx_obj = vfx_attr
                        else:
                            vfx_obj = getattr(cfg, 'extra', {}).get('vfx')
                    except Exception:
                        vfx_obj = None
                    if isinstance(vfx_obj, dict):
                        impact = vfx_obj.get('impact') or {}
                        if isinstance(impact, dict):
                            if isinstance(impact.get('preset'), str):
                                preset_id = impact.get('preset')
                            if isinstance(impact.get('ttl'), (int, float)):
                                ttl_ticks = int(impact.get('ttl'))
                            exp = impact.get('explosion') or {}
                            if isinstance(exp, dict):
                                if isinstance(exp.get('preset'), str):
                                    preset_id = exp.get('preset')
                                if isinstance(exp.get('ttl'), (int, float)):
                                    ttl_ticks = int(exp.get('ttl'))
                    if isinstance(preset_id, str) and preset_id:
                        x, y = hit_point_tile if hit_point_tile else (pos.x, pos.y)
                        eid2 = world.create_entity()
                        world.components.setdefault('Position', {})[eid2] = Position(x, y)
                        # Escalar impacto según multiplicador del proyectil
                        try:
                            smul = float(getattr(comp, 'vfx_scale_multiplier', 1.0))
                        except Exception:
                            smul = 1.0
                        world.components.setdefault('ParticlePresetComponent', {})[eid2] = ParticlePresetComponent(preset_id, scale_multiplier=smul)
                        world.components.setdefault('ExplosionComponent', {})[eid2] = ExplosionComponent(TimedEffectModel(ttl_ticks if ttl_ticks else 30))
                    else:
                        # Fallback: spawn native FireExplosionModel with advanced params if configured
                        try:
                            exp_cfg2 = None
                            if isinstance(vfx_obj, dict):
                                impact2 = vfx_obj.get('impact') or {}
                                if isinstance(impact2, dict):
                                    exp_cfg2 = impact2.get('explosion')
                            if isinstance(exp_cfg2, dict):
                                pcount = int(exp_cfg2.get('particle_count')) if isinstance(exp_cfg2.get('particle_count'), int) else 100
                                pscale = float(exp_cfg2.get('scale')) if isinstance(exp_cfg2.get('scale'), (int, float)) else 1.0
                                colors = None
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
                                else:
                                    gravity = None
                                drag = float(exp_cfg2.get('drag')) if isinstance(exp_cfg2.get('drag'), (int, float)) else None
                                blend_mode = exp_cfg2.get('blend_mode') if isinstance(exp_cfg2.get('blend_mode'), str) else None
                                sol = exp_cfg2.get('size_over_life') if isinstance(exp_cfg2.get('size_over_life'), (list, tuple)) else None
                                aol = exp_cfg2.get('alpha_over_life') if isinstance(exp_cfg2.get('alpha_over_life'), (list, tuple)) else None
                                col_ol = exp_cfg2.get('color_over_life') if isinstance(exp_cfg2.get('color_over_life'), (list, tuple)) else None
                                x2, y2 = hit_point_tile if hit_point_tile else (pos.x, pos.y)
                                eid3 = world.create_entity()
                                world.components.setdefault('Position', {})[eid3] = Position(x2, y2)
                                model3 = FireExplosionModel(
                                    x2,
                                    y2,
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
                                world.components.setdefault('ExplosionComponent', {})[eid3] = ExplosionComponent(model3)
                        except Exception:
                            pass
                except Exception:
                    pass
                world.remove_entity(eid)
                continue