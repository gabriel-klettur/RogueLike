import logging
from typing import Tuple

from roguelike_game.ecs.components.abilities.dash_component import DashComponent
from roguelike_game.ecs.components.particles.dash_emitter_component import DashEmitterComponent

from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world, direction_from_to


logger = logging.getLogger(__name__)


class HostileDashResolver(BaseSpellResolver):
    """Dash para hostiles: apunta al objetivo o dirección dada y usa partículas verdes anchas."""

    def _resolve_direction(self, world, caster, spawn_meta, camera) -> Tuple[float, float]:
        cx, cy = get_entity_center(world, caster)
        if isinstance(spawn_meta, dict):
            dir_override = spawn_meta.get('direction')
            if isinstance(dir_override, (list, tuple)) and len(dir_override) >= 2:
                dx, dy = float(dir_override[0]), float(dir_override[1])
                mag = (dx * dx + dy * dy) ** 0.5 or 1.0
                return dx / mag, dy / mag
            target_eid = spawn_meta.get('target_eid')
            if isinstance(target_eid, int) and target_eid in world.components.get('Position', {}):
                tx, ty = get_entity_center(world, target_eid)
                dx, dy, _ = direction_from_to(cx, cy, tx, ty)
                return dx, dy
        # fallback: mouse
        wx, wy = mouse_world(camera)
        dx, dy, _ = direction_from_to(cx, cy, wx, wy)
        return dx, dy

    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Dirección del dash
        dir_x, dir_y = self._resolve_direction(world, caster, spawn_meta, camera)
        speed = float(cfg.get('speed', 2200))
        duration = float(cfg.get('duration', 0.18))
        world.components.setdefault('DashComponent', {})[caster] = DashComponent(dir_x, dir_y, speed, duration)

        # Partículas verdes, más anchas y numerosas.
        # Leer posibles overrides desde cfg (flattened por SpellConfig)
        count = int(cfg.get('particle_count', 24) or 24)
        lifespan = int(cfg.get('particle_lifespan', 16) or 16)
        size_range = cfg.get('size_range', [4, 9]) or [4, 9]
        if not (isinstance(size_range, (list, tuple)) and len(size_range) >= 2):
            size_range = [4, 9]
        colors = cfg.get('particle_colors') or cfg.get('color') or [(0, 255, 120), (0, 200, 80), (0, 220, 100)]
        try:
            if isinstance(colors, (list, tuple)) and len(colors) >= 3 and all(isinstance(c, int) for c in colors):
                # single color -> expand into palette-ish tuple
                colors = [tuple(colors)]
            color_choices = tuple(tuple(c) for c in colors)
        except Exception:
            color_choices = ((0, 255, 120), (0, 200, 80), (0, 220, 100))
        speed_range = (1.5, 3.5)
        world.components.setdefault('DashEmitterComponent', {})[caster] = DashEmitterComponent(
            count=count,
            lifespan=lifespan,
            size_range=(int(size_range[0]), int(size_range[1])),
            color_choices=color_choices,
            speed_range=speed_range,
        )
        try:
            logger.info(
                "[HostileDashResolver] caster=%s dir=(%.2f,%.2f) speed=%.1f dur=%.2f count=%d size=%s colors=%s",
                caster, dir_x, dir_y, speed, duration, count, size_range, color_choices,
            )
        except Exception:
            pass
