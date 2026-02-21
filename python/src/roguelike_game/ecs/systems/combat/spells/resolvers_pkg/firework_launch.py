from .base import BaseSpellResolver
from .utils import get_entity_center, mouse_world
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.model import FireworkLaunchModel
from roguelike_game.ecs.components.abilities.firework_launch_component import FireworkLaunchComponent


class FireworkLaunchResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver para lanzamiento de fuegos artificiales
        cx, cy = get_entity_center(world, caster)
        wx, wy = mouse_world(camera)
        speed = cfg.get('speed', 0)
        model = FireworkLaunchModel(cx, cy, wx, wy, speed)
        world.components.setdefault('FireworkLaunchComponent', {})[caster] = FireworkLaunchComponent(model)
