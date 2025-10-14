from .base import BaseSpellResolver

from roguelike_game.ecs.systems.rendering.combat.spells.teleport.model import TeleportModel
from roguelike_game.ecs.components.abilities.teleport_component import TeleportComponent
from .utils import get_entity_center


class TeleportResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver for teleport effect
        cx, cy = get_entity_center(world, caster)
        direction = spawn_meta.get('direction', (1, 0))
        distance = cfg.get('distance', 200)
        end_x = cx + direction[0] * distance
        end_y = cy + direction[1] * distance
        lifespan = cfg.get('lifespan', 0.5)
        model = TeleportModel((cx, cy), (end_x, end_y), lifespan=lifespan)
        world.components.setdefault('TeleportComponent', {})[caster] = TeleportComponent(model)
