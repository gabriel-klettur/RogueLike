import logging
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import mouse_world
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.force_field_component import ForceFieldComponent


logger = logging.getLogger(__name__)


class VortexFieldResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # Determinar posición de spawn según prioridad:
        # 1) spawn_meta.spawn_pos
        # 2) spawn_at = 'mouse'|'caster' (spawn_meta o cfg)
        # 3) fallback centro del caster
        try:
            if isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                spawn_x, spawn_y = float(sx), float(sy)
            else:
                # Elegir origen según spawn_at
                spawn_at = None
                if isinstance(spawn_meta, dict):
                    spawn_at = spawn_meta.get('spawn_at', None)
                if not isinstance(spawn_at, str) or not spawn_at:
                    try:
                        effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
                    except Exception:
                        effect = {}
                    spawn_at = effect.get('spawn_at', cfg.get('spawn_at', 'caster'))
                spawn_at = str(spawn_at or 'caster').lower()
                if spawn_at == 'mouse' and camera is not None:
                    wx, wy = mouse_world(camera)
                    spawn_x, spawn_y = float(wx), float(wy)
                else:
                    cx, cy = get_entity_center(world, caster)
                    spawn_x, spawn_y = float(cx), float(cy)
        except Exception:
            pos_cmp = world.components.get('Position', {}).get(caster)
            if pos_cmp is not None:
                spawn_x, spawn_y = float(pos_cmp.x), float(pos_cmp.y)
            else:
                spawn_x, spawn_y = 0.0, 0.0

        # Leer parámetros desde cfg effect
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        radius = float(effect.get('radius', cfg.get('radius', 0.0)))
        force = float(effect.get('force', 0.0))
        mode = str(effect.get('mode', 'pull')).lower()
        duration = float(effect.get('duration', cfg.get('duration', 0.0)))
        # Seguimiento al caster: por defecto si spawn_at=='caster'
        default_follow = False
        try:
            default_follow = (str(effect.get('spawn_at', cfg.get('spawn_at', 'caster')) or 'caster').lower() == 'caster')
        except Exception:
            default_follow = False
        follow = default_follow
        # Overrides por spawn_meta y effect
        try:
            if isinstance(spawn_meta, dict) and 'follow_caster' in spawn_meta:
                follow = bool(spawn_meta.get('follow_caster'))
        except Exception:
            pass
        try:
            if 'follow_caster' in effect:
                follow = bool(effect.get('follow_caster'))
            elif 'follow_caster' in cfg:
                follow = bool(cfg.get('follow_caster'))
        except Exception:
            pass
        # Filtros opcionales de afectación y drag
        def _bool(v, dflt=False):
            try:
                return bool(v) if v is not None else dflt
            except Exception:
                return dflt
        affect_owner = _bool(effect.get('affect_owner', cfg.get('affect_owner')), False)
        affect_allies = _bool(effect.get('affect_allies', cfg.get('affect_allies')), False)
        affect_neutrals = _bool(effect.get('affect_neutrals', cfg.get('affect_neutrals')), False)
        affect_enemies = _bool(effect.get('affect_enemies', cfg.get('affect_enemies')), True)
        # Allow spawn_meta overrides
        if isinstance(spawn_meta, dict):
            if 'affect_owner' in spawn_meta: affect_owner = _bool(spawn_meta.get('affect_owner'), affect_owner)
            if 'affect_allies' in spawn_meta: affect_allies = _bool(spawn_meta.get('affect_allies'), affect_allies)
            if 'affect_neutrals' in spawn_meta: affect_neutrals = _bool(spawn_meta.get('affect_neutrals'), affect_neutrals)
            if 'affect_enemies' in spawn_meta: affect_enemies = _bool(spawn_meta.get('affect_enemies'), affect_enemies)
        try:
            drag = float(effect.get('drag', cfg.get('drag', 0.0)) or 0.0)
        except Exception:
            drag = 0.0

        # Crear entidad
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)
        world.components.setdefault('ForceFieldComponent', {})[eid] = ForceFieldComponent(
            radius=radius,
            force=force,
            mode=mode,
            duration=duration,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
            anchor_eid=(caster if follow else None),
            follow=follow,
            affect_owner=affect_owner,
            affect_allies=affect_allies,
            affect_neutrals=affect_neutrals,
            affect_enemies=affect_enemies,
            drag=drag,
        )
        try:
            logger.info("[VortexFieldResolver] caster=%s eid=%s pos=(%.1f,%.1f) radius=%.1f force=%.1f mode=%s dur=%.2f",
                        caster, eid, spawn_x, spawn_y, radius, force, mode, duration)
        except Exception:
            pass
