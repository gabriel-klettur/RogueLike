from dataclasses import dataclass, field
from typing import List


@dataclass
class SpawnerState:
    """Mutable runtime state for a spawner entity (MVP).

    - started: whether the spawner is active (triggered)
    - current_wave_idx: index of current wave (MVP uses 0)
    - cooldown_remaining: frames remaining until next spawn batch
    - spawned_entities: optional list of entity ids spawned by this spawner (not enforced in MVP)
    """
    started: bool = False
    current_wave_idx: int = 0
    cooldown_remaining: int = 0
    spawned_entities: List[int] = field(default_factory=list)
