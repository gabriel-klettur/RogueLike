import logging
import math
from roguelike_game.config.particles_config import get_preset as _get_particle_preset
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.components.particles.slash_emitter_component import SlashEmitterComponent

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to, spawn_at_offset


logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass


class SlashResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Recalcular centro del caster
        spawn_offset = spawn_meta.get('offset', 0)
        cfg_offset = cfg.get('offset', 0)
        offset = spawn_offset + cfg_offset
        cx, cy = get_entity_center(world, caster)
        # Dirección: permitir override desde spawn_meta (NPC) o usar mouse (Player)
        dir_override = spawn_meta.get('direction') if isinstance(spawn_meta, dict) else None
        target_eid = spawn_meta.get('target_eid') if isinstance(spawn_meta, dict) else None
        if isinstance(dir_override, (list, tuple)) and len(dir_override) >= 2:
            dx_raw, dy_raw = float(dir_override[0]), float(dir_override[1])
        elif isinstance(target_eid, int) and target_eid in world.components.get('Position', {}):
            tx, ty = get_entity_center(world, target_eid)
            dx_raw, dy_raw, _ = direction_from_to(cx, cy, tx, ty)
        else:
            wx, wy = mouse_world(camera)
            dx_raw, dy_raw, _ = direction_from_to(cx, cy, wx, wy)
        dir_x, dir_y = dx_raw, dy_raw
        # Parámetros de configuración (con soporte de preset en vfx.preset)
        # Resolver defaults desde particles preset, y aplicar overrides del spell.
        base: dict = {}
        overrides: dict = {}
        # 1) Preset desde cfg.vfx si es string, o desde vfx_obj['preset'] si es dict
        try:
            vfx_attr = getattr(cfg, 'vfx', None)
        except Exception:
            vfx_attr = None
        preset_id = None
        if isinstance(vfx_attr, str):
            preset_id = vfx_attr
        elif isinstance(vfx_attr, dict):
            pid = vfx_attr.get('preset')
            if isinstance(pid, str):
                preset_id = pid
        else:
            try:
                vfx_obj_tmp = getattr(cfg, 'extra', {}).get('vfx')
                if isinstance(vfx_obj_tmp, dict):
                    pid = vfx_obj_tmp.get('preset')
                    if isinstance(pid, str):
                        preset_id = pid
            except Exception:
                pass
        if isinstance(preset_id, str):
            try:
                p = _get_particle_preset(preset_id)
                if p and isinstance(getattr(p, 'vfx', None), dict):
                    pv = p.vfx.get('particles')
                    if isinstance(pv, dict):
                        base = dict(pv)
            except Exception:
                base = {}
        # 2) Overrides en vfx.particles dentro del spell
        try:
            vfx_obj2 = vfx_attr if isinstance(vfx_attr, dict) else (getattr(cfg, 'extra', {}).get('vfx'))
            if isinstance(vfx_obj2, dict):
                pov = vfx_obj2.get('particles')
                if isinstance(pov, dict):
                    overrides = dict(pov)
        except Exception:
            overrides = {}
        parts = {**base, **overrides}

        # Asignar con prioridad (evitando que los defaults del dataclass tapen al preset):
        # 1) Si cfg trae un valor "real" (no default/sentinel), usarlo.
        # 2) Si no, usar parts (preset/overrides).
        # 3) Si no hay, usar defaults razonables.
        # Radio/arco VISUAL (partículas): siguen usando los valores clásicos
        vis_radius = cfg.get('radius', None)
        if not isinstance(vis_radius, (int, float)) or vis_radius <= 0:
            vis_radius = parts.get('radius', 0)
        vis_arc_deg = cfg.get('arc_range_degrees', None)
        if not isinstance(vis_arc_deg, (int, float)) or vis_arc_deg <= 0:
            vis_arc_deg = parts.get('arc_range_degrees', 120)
        vis_arc_range = math.radians(vis_arc_deg)

        # Radio/arco de IMPACTO (hitbox): opcionales y desacoplados
        hit_radius = cfg.get('hit_radius', None)
        if not isinstance(hit_radius, (int, float)) or hit_radius <= 0:
            hit_radius = vis_radius
        hit_arc_deg = cfg.get('hit_arc_degrees', None)
        if not isinstance(hit_arc_deg, (int, float)) or hit_arc_deg <= 0:
            hit_arc_deg = vis_arc_deg
        hit_arc_range = math.radians(hit_arc_deg)
        # Número de partículas por “golpe”
        count = cfg.get('particle_count', None)
        if not isinstance(count, int) or count <= 0:
            count = parts.get('count', 18)
        # Vida
        lifespan = cfg.get('lifespan', None)
        if not isinstance(lifespan, (int, float)) or lifespan <= 0:
            lifespan = parts.get('lifespan', 15)
        # Tamaño
        sr = cfg.get('size_range', None)
        # Tratar [1,1] (default del dataclass) como sentinel -> usa preset
        if not (isinstance(sr, (list, tuple)) and len(sr) >= 2) or (float(sr[0]) == 1.0 and float(sr[1]) == 1.0):
            sr = parts.get('size_range', [2, 6])
        if not (isinstance(sr, (list, tuple)) and len(sr) >= 2):
            sr = [2, 6]
        size_min, size_max = int(sr[0]), int(sr[1])
        # Color (una sola o paleta)
        base_color = cfg.get('color', None)
        # Tratar [255,255,255] (default del dataclass) como sentinel -> usa preset si existe
        if not (isinstance(base_color, (list, tuple)) and len(base_color) >= 3) or \
           (int(base_color[0]) == 255 and int(base_color[1]) == 255 and int(base_color[2]) == 255):
            if isinstance(parts.get('color'), (list, tuple)) and len(parts.get('color')) >= 3:
                base_color = parts.get('color')
            elif isinstance(parts.get('colors'), (list, tuple)) and parts.get('colors'):
                # primera de la paleta
                base_color = parts.get('colors')[0]
            else:
                base_color = [255, 230, 150]
        # Velocidad
        speed_mult = cfg.get('speed_multiplier', None)
        if not isinstance(speed_mult, (int, float)) or speed_mult <= 0:
            speed_mult = parts.get('speed', 1.0)

        # Log de diagnóstico: parámetros efectivos
        try:
            logger.info(
                "[SlashResolver] caster=%s preset=%s vis_radius=%s vis_arc_deg=%s hit_radius=%s hit_arc_deg=%s count=%s life=%s size=%s color=%s speed_mult=%s",
                caster, str(preset_id), vis_radius, vis_arc_deg, hit_radius, hit_arc_deg, count, lifespan, (size_min, size_max), base_color, speed_mult
            )
        except Exception:
            pass
        # Registrar hitbox de slash para colisión (usa SOLO hit_radius/hit_arc_range)
        # Ser tolerante en tests donde el mundo no implementa create_entity
        real_x, real_y = cx, cy
        try:
            hb_id = world.create_entity()
            real_x, real_y = spawn_at_offset(cx, cy, dir_x, dir_y, offset)
            world.components['Position'][hb_id] = Position(real_x, real_y)
            # Rotate behavior: allow override so NPC slashes don't rotate with mouse
            rotate_with_owner = True
            try:
                rwo = spawn_meta.get('rotate_with_owner') if isinstance(spawn_meta, dict) else None
                if isinstance(rwo, bool):
                    rotate_with_owner = rwo
            except Exception:
                pass
            world.components['HitboxComponent'][hb_id] = HitboxComponent(
                owner=caster,
                offset=offset,
                radius=hit_radius,
                arc_angle=hit_arc_range,
                direction=(dir_x, dir_y),
                lifespan=lifespan,
                damage=cfg.get('damage', 0),
                follow_owner=True,
                rotate_with_owner=rotate_with_owner,
            )
        except Exception:
            # Sin hitbox en entornos reducidos, continuar con partículas
            pass
        # Añadir emisor de partículas de slash (usa SOLO vis_radius/vis_arc_range)
        world.components.setdefault('SlashEmitterComponent', {})[caster] = SlashEmitterComponent(
            radius=vis_radius,
            arc_range=vis_arc_range,
            count=count,
            lifespan=lifespan,
            size_range=(size_min, size_max),
            color=tuple(base_color),
            speed_multiplier=speed_mult,
            direction=(dir_x, dir_y),
            offset=offset
        )
        try:
            logger.info("[SlashResolver] registered SlashEmitterComponent for caster=%s at pos=(%.1f,%.1f)", caster, real_x, real_y)
        except Exception:
            pass
