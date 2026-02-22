import logging
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.chain_lightning_component import ChainLightningComponent


logger = logging.getLogger(__name__)


class ChainLightningResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        try:
            # Determinar posición de inicio
            # Prioridad 1: posición del ratón (pedida por diseño)
            if camera is not None:
                wx, wy = mouse_world(camera)
                start_x, start_y = float(wx), float(wy)
            elif isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                start_x, start_y = float(sx), float(sy)
            else:
                cx, cy = get_entity_center(world, caster)
                start_x, start_y = float(cx), float(cy)
        except Exception:
            pos = world.components.get('Position', {}).get(caster)
            if pos is not None:
                start_x, start_y = float(pos.x), float(pos.y)
            else:
                start_x, start_y = 0.0, 0.0

        # Efectos desde cfg plano y cfg.extra.effect
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        damage = float(cfg.get('damage', effect.get('damage', 0)))
        max_bounces = int(effect.get('max_bounces', 0))
        rng = float(effect.get('range', cfg.get('range', 0)))
        damage_decay = float(effect.get('damage_decay', 1.0))

        # Crear entidad de chain lightning
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(start_x, start_y)
        world.components.setdefault('ChainLightningComponent', {})[eid] = ChainLightningComponent(
            start_pos=(start_x, start_y),
            damage=damage,
            max_bounces=max_bounces,
            range=rng,
            damage_decay=damage_decay,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
        )
        try:
            logger.info("[ChainLightningResolver] caster=%s eid=%s pos=(%.1f,%.1f) dmg=%.1f bounces=%d range=%.1f decay=%.2f",
                        caster, eid, start_x, start_y, damage, max_bounces, rng, damage_decay)
        except Exception:
            pass
