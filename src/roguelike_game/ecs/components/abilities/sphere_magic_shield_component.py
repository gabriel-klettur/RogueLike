from dataclasses import dataclass
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield.model import SphereMagicShieldModel

@dataclass
class SphereMagicShieldComponent:
    """
    ECS component that wraps SphereMagicShieldModel for shield effect.
    """
    model: SphereMagicShieldModel
