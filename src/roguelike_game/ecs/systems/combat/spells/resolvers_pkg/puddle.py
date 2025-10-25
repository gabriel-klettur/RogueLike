import logging
import pygame
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent


logger = logging.getLogger(__name__)
try:
    logger.setLevel(logging.INFO)
except Exception:
    pass


class PuddleResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        # Determinar posición de spawn
        try:
            if isinstance(spawn_meta, dict) and 'spawn_pos' in spawn_meta:
                sx, sy = spawn_meta.get('spawn_pos', (0, 0))
                spawn_x, spawn_y = float(sx), float(sy)
            else:
                cx, cy = get_entity_center(world, caster)
                spawn_x, spawn_y = float(cx), float(cy)
        except Exception:
            # Fallback simple
            pos_cmp = world.components.get('Position', {}).get(caster)
            if pos_cmp is not None:
                spawn_x, spawn_y = float(pos_cmp.x), float(pos_cmp.y)
            else:
                spawn_x, spawn_y = 0.0, 0.0

        # Parametrización: campos aplanados + extra.effect (tolerante)
        effect = {}
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        radius = float(cfg.get('radius', effect.get('radius', 80)))
        duration = float(cfg.get('duration', effect.get('duration', 5.0)))
        # tick_period aún no está en el aplanado estándar: leer desde effect o usar default
        tick_period = float(effect.get('tick_period', 0.25))
        damage = float(cfg.get('damage', effect.get('damage', 0)))
        heal = float(effect.get('heal', 0))
        status = effect.get('status')
        move_speed_mult = float(effect.get('move_speed_mult', 1.0))
        element = effect.get('element')

        # Color base desde vfx.particles.color o paleta (opcional)
        color = None
        alpha = 80
        try:
            vfx = getattr(cfg, 'extra', {}).get('vfx', {}) or {}
            parts = vfx.get('particles', {}) or {}
            if isinstance(parts.get('color'), (list, tuple)):
                color = tuple(parts['color'])
            elif isinstance(parts.get('colors'), (list, tuple)) and parts['colors']:
                color = tuple(parts['colors'][0])
            if isinstance(vfx.get('alpha'), (int, float)):
                alpha = int(vfx['alpha'])
        except Exception:
            pass

        # Crear entidad puddle
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)

        # Si hay sprite, adjuntar para un decal de piso
        sprite_path = cfg.get('sprite', None)
        if sprite_path:
            try:
                img = pygame.image.load(sprite_path).convert_alpha()
                world.components.setdefault('Sprite', {})[eid] = Sprite(img)
                world.components.setdefault('Scale', {})[eid] = Scale(scale=cfg.get('scale', 1.0))
            except Exception:
                pass

        # Registrar componente de lógica
        world.components.setdefault('PuddleComponent', {})[eid] = PuddleComponent(
            radius=radius,
            duration=duration,
            tick_period=tick_period,
            damage=damage,
            heal=heal,
            status=status,
            move_speed_mult=move_speed_mult,
            element=element,
            color=color,
            alpha=alpha,
            owner=caster,
            spell_key=getattr(cfg, 'key', ''),
        )

        try:
            logger.info("[PuddleResolver] caster=%s eid=%s pos=(%.1f,%.1f) r=%.1f dur=%.2f tick=%.2f dmg=%.1f heal=%.1f",
                        caster, eid, spawn_x, spawn_y, radius, duration, tick_period, damage, heal)
        except Exception:
            pass
