from pygame.math import Vector2

from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.rendering.combat.spells.smoke.model import SmokeModel
from roguelike_game.ecs.components.abilities.smoke_component import SmokeComponent


class SmokeResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver para efecto de humo
        cx, cy = get_entity_center(world, caster)
        direction = spawn_meta.get('direction', (1, 0))
        dir_vec = Vector2(direction[0], direction[1])
        num_particles = cfg.get('particle_count', 15)
        model = SmokeModel(cx, cy, dir_vec, num_particles)
        world.components.setdefault('SmokeComponent', {})[caster] = SmokeComponent(model)
