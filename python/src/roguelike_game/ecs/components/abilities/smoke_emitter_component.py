from dataclasses import dataclass
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import SmokeEmitterModel

@dataclass
class SmokeEmitterComponent:
    """
    ECS component that wraps legacy SmokeEmitterModel for smoke emitter effect.
    """
    model: SmokeEmitterModel
