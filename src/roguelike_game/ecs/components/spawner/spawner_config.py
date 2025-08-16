from dataclasses import dataclass
from typing import Any, Dict, List, Tuple


@dataclass
class SpawnerConfig:
    """Immutable configuration for a map spawner, resolved from template + overrides.

    Fields follow the JSON template structure (MVP):
    - template_id: id of the template used
    - zone: map zone identifier where this spawner belongs
    - anchor_tile: (tx, ty) tile position for spawning
    - spawner_type: "invisible" | "building" (MVP uses "invisible")
    - trigger: dict with at least { type: "proximity", radius: int, auto_start: bool }
    - policy: dict with at least { mode: "periodic", cooldown_s: float, max_active: int, persistent: bool }
    - waves: list of dicts, each with { spawns: [ { kind: "monster"|"item", id: str, count: int, spread_radius: int } ] }
    - cooldown_frames: derived from policy.cooldown_s using 60 FPS assumption
    """
    template_id: str
    zone: str
    anchor_tile: Tuple[int, int]
    spawner_type: str
    trigger: Dict[str, Any]
    policy: Dict[str, Any]
    waves: List[Dict[str, Any]]
    cooldown_frames: int = 0
