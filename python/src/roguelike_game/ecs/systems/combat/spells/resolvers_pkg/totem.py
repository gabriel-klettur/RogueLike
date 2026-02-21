from typing import Any, Dict
import pygame
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.base import BaseSpellResolver
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center, mouse_world
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.abilities.totem_component import TotemComponent
from roguelike_game.ecs.components.rendering.sprite import Sprite


class TotemResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        try:
            effect = getattr(cfg, 'extra', {}).get('effect', {}) or {}
        except Exception:
            effect = {}
        radius = float(effect.get('radius', cfg.get('radius', 0.0) or 0.0))
        duration = float(effect.get('duration', cfg.get('duration', 0.0) or 0.0))
        tick_period = float(effect.get('tick_period', 0.5))
        kind = str(effect.get('kind', 'heal'))
        val = float(effect.get('heal_per_tick', effect.get('damage_per_tick', 0.0)))
        # Base spawn position: for player, use mouse world; otherwise caster center
        try:
            player_eid = getattr(world, 'player_entity', None)
        except Exception:
            player_eid = None
        if player_eid is not None and caster == player_eid:
            cx, cy = mouse_world(camera)
        else:
            cx, cy = get_entity_center(world, caster)
        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(cx, cy)
        world.components.setdefault('TotemComponent', {})[eid] = TotemComponent(
            radius=radius,
            duration=duration,
            tick_period=tick_period,
            kind=kind,
            value=val,
            owner=caster,
        )
        # Visual: Triángulo amarillo para representar el totem
        try:
            w, h = 24, 22
            surf = pygame.Surface((w, h), pygame.SRCALPHA)
            color = (255, 230, 0, 255)
            points = [(w // 2, 0), (0, h - 1), (w - 1, h - 1)]
            pygame.draw.polygon(surf, color, points)
            world.components.setdefault('Sprite', {})[eid] = Sprite(surf)
        except Exception:
            pass
        return eid
