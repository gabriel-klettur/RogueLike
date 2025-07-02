from dataclasses import dataclass
from roguelike_game.systems.effects.spells.smoke_emitter.model import SmokeEmitterModel

@dataclass
class SmokeEmitterComponent:
    """
    ECS component that wraps legacy SmokeEmitterModel for smoke emitter effect.
    """
    model: SmokeEmitterModel
