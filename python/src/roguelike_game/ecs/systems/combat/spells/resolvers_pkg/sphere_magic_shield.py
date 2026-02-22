from .base import BaseSpellResolver
from .utils import get_entity_center
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield.model import SphereMagicShieldModel
from roguelike_game.ecs.components.abilities.sphere_magic_shield_component import SphereMagicShieldComponent


class SphereMagicShieldResolver(BaseSpellResolver):
    def resolve(self, world, caster, spawn_meta, cfg, camera):
        # Resolver for sphere magic shield spell
        cx, cy = get_entity_center(world, caster)
        model = SphereMagicShieldModel(cx, cy,
                                       radius=cfg.get('radius', 80),
                                       duration=cfg.get('duration', 5.0))
        world.components.setdefault('SphereMagicShieldComponent', {})[caster] = SphereMagicShieldComponent(model)
