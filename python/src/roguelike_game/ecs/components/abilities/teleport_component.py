from dataclasses import dataclass
from roguelike_game.ecs.systems.rendering.combat.spells.teleport.model import TeleportModel

@dataclass
class TeleportComponent:
    """
    ECS component that wraps TeleportModel for teleport effect.
    """
    model: TeleportModel
