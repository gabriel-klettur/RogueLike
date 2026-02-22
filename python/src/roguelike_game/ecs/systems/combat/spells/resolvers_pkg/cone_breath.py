import logging
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.cone_breath_component import ConeBreathComponent


logger = logging.getLogger(__name__)


class ConeBreathResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # Determinar centro del caster
        cx, cy = get_entity_center(world, caster)
        # Dirección inicial: permitir override para NPCs
        dir_override = spawn_meta.get('direction') if isinstance(spawn_meta, dict) else None
        target_eid = spawn_meta.get('target_eid') if isinstance(spawn_meta, dict) else None
        initial_dir = None
        if isinstance(dir_override, (list, tuple)) and len(dir_override) >= 2:
            dx_raw, dy_raw = float(dir_override[0]), float(dir_override[1])
            initial_dir = (dx_raw, dy_raw)
        elif isinstance(target_eid, int) and target_eid in world.components.get('Position', {}):
            tx, ty = get_entity_center(world, target_eid)
            dx_raw, dy_raw, _ = direction_from_to(cx, cy, tx, ty)
            initial_dir = (dx_raw, dy_raw)
        # Lectura de parámetros desde cfg.effect y fallback a cfg plano
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        arc_deg = float(effect.get('arc_range_degrees', cfg.get('arc_range_degrees', 0)))
        length = float(effect.get('length', cfg.get('range', cfg.get('radius', 0))))
        # damage por tick: aceptar 'damage_per_tick' o fallback a 'dps' o 'damage'
        dpt = effect.get('damage_per_tick', None)
        if not isinstance(dpt, (int, float)):
            dpt = effect.get('dps', cfg.get('damage', 0))
        damage_per_tick = float(dpt or 0)
        tick_period = float(effect.get('tick_period', 0.2))
        duration = float(effect.get('duration', cfg.get('duration', 0)))
        # Elemento/Status opcional
        element = str(effect.get('element', cfg.get('element', ''))) if (isinstance(effect.get('element', None), (str,))) or (isinstance(cfg.get('element', None), (str,))) else ''
        status = effect.get('status') if isinstance(effect.get('status'), dict) else None
        # Flags de seguimiento/rotación
        follow_owner = True
        rotate_with_owner = True
        try:
            if isinstance(spawn_meta, dict):
                if 'follow_owner' in spawn_meta:
                    follow_owner = bool(spawn_meta['follow_owner'])
                if 'rotate_with_owner' in spawn_meta:
                    rotate_with_owner = bool(spawn_meta['rotate_with_owner'])
        except Exception:
            pass
        # Offset opcional desde cfg o spawn_meta
        off = 0.0
        try:
            off = float(spawn_meta.get('offset', 0.0) if isinstance(spawn_meta, dict) else 0.0)
        except Exception:
            off = 0.0
        try:
            co = cfg.get('offset', None)
            if isinstance(co, (int, float)):
                off += float(co)
        except Exception:
            pass
        # Crear entidad ConeBreath anclada
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(cx, cy)
        world.components.setdefault('ConeBreathComponent', {})[eid] = ConeBreathComponent(
            owner=caster,
            arc_degrees=arc_deg,
            length=length,
            damage_per_tick=damage_per_tick,
            tick_period=tick_period,
            duration=duration,
            spell_key=getattr(cfg, 'key', ''),
            preset_id=(cfg.get('vfx') if isinstance(cfg.get('vfx'), str) else None),
            preset_scale=float(cfg.get('scale', 1.0) or 1.0),
            follow_owner=follow_owner,
            rotate_with_owner=rotate_with_owner,
            offset=off,
            initial_direction=initial_dir,
            element=element,
            status=status,
        )
        try:
            logger.info(
                "[ConeBreathResolver] caster=%s eid=%s arc=%.1f length=%.1f dpt=%.1f tper=%.2f dur=%.2f",
                caster, eid, arc_deg, length, damage_per_tick, tick_period, duration
            )
        except Exception:
            pass
