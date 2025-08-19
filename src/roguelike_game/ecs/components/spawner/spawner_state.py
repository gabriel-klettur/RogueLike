from dataclasses import dataclass, field
from typing import List, Set


@dataclass
class SpawnerState:
    """Mutable runtime state for a spawner entity (MVP).

    - started: whether the spawner is active (triggered)
    - current_wave_idx: index of current wave (MVP uses 0)
    - cooldown_remaining: frames remaining until next spawn batch
    - spawned_entities: optional list of entity ids spawned by this spawner (not enforced in MVP)
    - spawned_this_wave: if True, current wave has been spawned and we're waiting for completion
    - current_wave_entities: live entity ids belonging to the current wave
    - expected_this_wave: number of entities expected to be spawned for the current wave
    - finished: whether all waves have completed
    """
    started: bool = False
    current_wave_idx: int = 0
    cooldown_remaining: int = 0
    spawned_entities: List[int] = field(default_factory=list)
    spawned_this_wave: bool = False
    current_wave_entities: Set[int] = field(default_factory=set)
    expected_this_wave: int = 0
    finished: bool = False
