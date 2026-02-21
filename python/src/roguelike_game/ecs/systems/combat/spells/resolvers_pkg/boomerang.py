import pygame
from typing import Any, Dict

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.rendering.sprite import Sprite
from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.abilities.boomerang_component import BoomerangComponent


class BoomerangResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta: Dict[str, Any], cfg, camera):
        cx, cy = get_entity_center(world, caster)
        used_mouse = False
        if isinstance(spawn_meta, dict):
            spawn = spawn_meta.get('spawn_pos')
            if isinstance(spawn, (list, tuple)) and len(spawn) >= 2:
                spawn_x, spawn_y = float(spawn[0]), float(spawn[1])
            else:
                spawn_x, spawn_y = float(cx), float(cy)
            d = spawn_meta.get('direction')
            if isinstance(d, (list, tuple)) and len(d) >= 2:
                dx, dy = float(d[0]), float(d[1])
                mag = (dx*dx + dy*dy) ** 0.5 or 1.0
                dir_x, dir_y = dx/mag, dy/mag
            else:
                wx, wy = mouse_world(camera)
                dir_x, dir_y, _ = direction_from_to(spawn_x, spawn_y, wx, wy)
                used_mouse = True
        else:
            spawn_x, spawn_y = float(cx), float(cy)
            wx, wy = mouse_world(camera)
            dir_x, dir_y, _ = direction_from_to(spawn_x, spawn_y, wx, wy)
            used_mouse = True

        speed = float(cfg.get('speed', 0))
        damage = float(cfg.get('damage', 0))
        max_range = float(cfg.get('range', 0))
        hit_radius = float(cfg.get('hit_radius', 12.0))
        try:
            eff = getattr(cfg, 'extra', {}).get('effect', {}) or {}
            if 'return_speed' in eff:
                return_speed = float(eff.get('return_speed'))
            else:
                return_speed = speed
            passes_through = bool(eff.get('passes_through', False))
            if 'hit_radius' in eff and hit_radius == 12.0:
                hit_radius = float(eff.get('hit_radius'))
        except Exception:
            return_speed = speed
            passes_through = False

        # Determine effective range: distance to mouse if available, clamped by configured max_range when set
        eff_range = max_range
        try:
            if used_mouse and camera is not None:
                wx, wy = mouse_world(camera)
                dxm = float(wx) - float(spawn_x)
                dym = float(wy) - float(spawn_y)
                dist_mouse = (dxm*dxm + dym*dym) ** 0.5
                if max_range and max_range > 0:
                    eff_range = min(float(max_range), float(dist_mouse))
                else:
                    eff_range = float(dist_mouse)
        except Exception:
            pass
        if not isinstance(eff_range, (int, float)) or eff_range <= 0:
            eff_range = max_range

        eid = world.create_entity()
        world.components.setdefault('Position', {})[eid] = Position(spawn_x, spawn_y)
        world.components.setdefault('Velocity', {})[eid] = Velocity(dir_x * speed, dir_y * speed)
        world.components.setdefault('BoomerangComponent', {})[eid] = BoomerangComponent(
            dir_x, dir_y, speed, damage, eff_range, return_speed, passes_through, caster, (spawn_x, spawn_y), hit_radius=hit_radius, spell_key=getattr(cfg, 'key', None)
        )

        sprite_path = cfg.get('sprite', None)
        if sprite_path:
            try:
                img = pygame.image.load(sprite_path).convert_alpha()
                world.components.setdefault('Sprite', {})[eid] = Sprite(img)
                world.components.setdefault('Scale', {})[eid] = Scale(scale=cfg.get('scale', 1.0))
            except Exception:
                pass
