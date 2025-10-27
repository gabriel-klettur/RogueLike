import logging
import pygame
from typing import Any, Dict, Optional

from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.abilities.mine_component import MineComponent


logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass


class MineResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # Determinar posición de spawn (o centro del caster)
        try:
            if isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                spawn_x, spawn_y = float(sx), float(sy)
            else:
                cx, cy = get_entity_center(world, caster)
                spawn_x, spawn_y = float(cx), float(cy)
        except Exception:
            pos_cmp = world.components.get('Position', {}).get(caster)
            if pos_cmp is not None:
                spawn_x, spawn_y = float(pos_cmp.x), float(pos_cmp.y)
            else:
                spawn_x, spawn_y = 0.0, 0.0

        # Extraer efecto desde cfg (tolerante a formatos)
        effect: Dict[str, Any] = {}
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        trigger_radius = float(effect.get('trigger_radius', cfg.get('radius', 60)))
        arming_time = float(effect.get('arming_time', 0.5))
        ttl = float(effect.get('ttl', effect.get('duration', 12.0)))
        payload = effect.get('payload', {}) or {}

        # Crear entidad mina
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)

        # Sprite opcional (de vfx.sprite o cfg.sprite)
        sprite_path: Optional[str] = None
        try:
            vfx = getattr(cfg, 'extra', {}).get('vfx', {}) or {}
            sp = vfx.get('sprite') if isinstance(vfx, dict) else None
            if isinstance(sp, dict) and isinstance(sp.get('path'), str):
                sprite_path = sp.get('path')
        except Exception:
            sprite_path = None
        if not sprite_path:
            sprite_path = cfg.get('sprite', None)
        if sprite_path:
            try:
                img = pygame.image.load(sprite_path).convert_alpha()
                world.components.setdefault('Sprite', {})[eid] = Sprite(img)
                world.components.setdefault('Scale', {})[eid] = Scale(scale=cfg.get('scale', 1.0))
            except Exception:
                pass

        # Registrar componente de mina
        world.components.setdefault('MineComponent', {})[eid] = MineComponent(
            trigger_radius=trigger_radius,
            arming_time=arming_time,
            ttl=ttl,
            payload=payload,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
        )

        try:
            logger.info("[MineResolver] caster=%s eid=%s pos=(%.1f,%.1f) r=%.1f arm=%.2f ttl=%.2f",
                        caster, eid, spawn_x, spawn_y, trigger_radius, arming_time, ttl)
        except Exception:
            pass
