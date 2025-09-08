from dataclasses import dataclass
from typing import Any, Dict, List, Tuple, Optional


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
    - restart_cooldown_frames: derived from policy.restart_cooldown_s using 60 FPS (falls back to cooldown_s if not provided)
    - between_waves_cooldown_frames: optional. If >0, after a wave is cleared, the next wave starts after this fixed cooldown
      regardless of proximity (when used with proximity_initial_only policy). Defaults to 0 (fallback to cooldown_frames).
    - spawn_radius: optional. If None/0 -> center-first spiral (default). If int>0 -> pick spots randomly within that tile radius.
      If string in {"random", "aleatorio", "aleatoreo"} -> random within per-wave fallback_max.
    - spawner_shape: optional. "circle" (default) or "square". When random placement is used (via spawn_radius),
      defines whether the area is a circle or the circumscribed square of the same radius.
    - defend_spawn: optional. If True, spawned NPCs will defend the spawner area (circle defined by spawn_radius).
    - defend_leash: optional. If True (default), defenders are leashed to the defend circle; if False, they won't leash back.
    - visible_in_game: optional. If True and a building_id is provided, the spawner will link to that building at runtime.
    - building_id: optional. If provided, identifies the Building instance used as the runtime visual for this spawner.
    - state_visuals: optional. Mapping from FSM state id (e.g., "AwaitTrigger", "SpawningWave", "WaitCooldown",
      "WaitRestart", "Finished", etc.) to a building_id to use while in that state. If present, it takes precedence
      over the top-level building_id when the spawner is in that state.
    """
    template_id: str
    zone: str
    anchor_tile: Tuple[int, int]
    spawner_type: str
    trigger: Dict[str, Any]
    policy: Dict[str, Any]
    waves: List[Dict[str, Any]]
    cooldown_frames: int = 0
    restart_cooldown_frames: int = 0
    between_waves_cooldown_frames: int = 0
    # New placement mode configuration
    spawn_radius: Optional[Any] = None
    # Shape for random placement area when spawn_radius is used (tiles): "circle" | "square"
    spawner_shape: str = "circle"
    # Defense behavior toggle: NPCs guard the spawn circle
    defend_spawn: bool = False
    # Whether defenders should be leashed back into the circle
    defend_leash: bool = True
    # If True and building_id is set, link to a building visual in the normal renderer
    visible_in_game: bool = False
    # Linkage to a Building visual by its persistent id (from buildings_instances.json)
    building_id: Optional[int] = None
    # Optional per-state visuals mapping: { state_id: building_id }
    state_visuals: Optional[Dict[str, int]] = None
