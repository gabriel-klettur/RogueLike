import logging
import random
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import mouse_world
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.meteor_shower_component import MeteorShowerComponent

logger = logging.getLogger(__name__)

class MeteorShowerResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        try:
            if isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                spawn_x, spawn_y = float(sx), float(sy)
            else:
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

        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}

        count = int(effect.get('count', 1))
        interval = float(effect.get('interval', 0.25))
        area_radius = float(effect.get('area_radius', effect.get('radius', cfg.get('radius', 0.0))))
        impact_damage = float(effect.get('impact_damage', effect.get('damage', cfg.get('damage', 0.0))))
        impact_radius = float(effect.get('impact_radius', effect.get('explosion_radius', 120.0)))

        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)
        world.components.setdefault('MeteorShowerComponent', {})[eid] = MeteorShowerComponent(
            count=count,
            interval=interval,
            area_radius=area_radius,
            impact_damage=impact_damage,
            impact_radius=impact_radius,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
        )
        try:
            logger.info("[MeteorShowerResolver] caster=%s eid=%s pos=(%.1f,%.1f) count=%s interval=%.2f area=%.1f dmg=%.1f r=%.1f",
                        caster, eid, spawn_x, spawn_y, count, interval, area_radius, impact_damage, impact_radius)
        except Exception:
            pass
