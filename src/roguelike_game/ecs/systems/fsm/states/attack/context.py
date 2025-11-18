"""Shared context helpers for the melee attack state."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Optional, Tuple

from roguelike_game.ecs.utils.position_utils import compute_entity_center


Vector2 = Tuple[float, float]


@dataclass
class PositionSnapshot:
    """Stores spatial information between an NPC and the player."""

    origin: Vector2
    target: Vector2
    delta: Vector2
    distance_sq: float


@dataclass
class AttackEnvironment:
    """Convenience wrapper over ECS world access during AttackState.

    Provides safe component lookups while keeping attribute access terse.
    """

    world: Any
    entity_id: int
    now: float

    @property
    def player_id(self) -> Optional[int]:
        return getattr(self.world, "player_entity", None)

    def get_component(self, name: str) -> Optional[Any]:
        return self.world.components.get(name, {}).get(self.entity_id)

    def remove_component(self, name: str) -> None:
        self.world.components.get(name, {}).pop(self.entity_id, None)

    def player_component(self, name: str) -> Optional[Any]:
        if self.player_id is None:
            return None
        return self.world.components.get(name, {}).get(self.player_id)

    def compute_positions(self) -> Optional[PositionSnapshot]:
        pos_map = self.world.components.get("Position", {})
        npc_pos = pos_map.get(self.entity_id)
        target_pos = pos_map.get(self.player_id) if self.player_id is not None else None
        if npc_pos is None or target_pos is None:
            return None

        spr_map = self.world.components.get("Sprite", {})
        scl_map = self.world.components.get("Scale", {})
        npc_sprite = spr_map.get(self.entity_id)
        npc_scale = scl_map.get(self.entity_id)
        target_sprite = spr_map.get(self.player_id) if self.player_id is not None else None
        target_scale = scl_map.get(self.player_id) if self.player_id is not None else None

        origin = _extract_center(npc_pos, npc_sprite, npc_scale)
        target = _extract_center(target_pos, target_sprite, target_scale)
        dx = target[0] - origin[0]
        dy = target[1] - origin[1]
        dist_sq = dx * dx + dy * dy
        return PositionSnapshot(origin=origin, target=target, delta=(dx, dy), distance_sq=dist_sq)

    def is_player_defeated(self) -> bool:
        health = self.player_component("Health")
        if health is None:
            return True
        return getattr(health, "current_hp", 0) <= 0

    def player_has_death_timer(self) -> bool:
        if self.player_id is None:
            return False
        return self.player_id in self.world.components.get("DeathTimer", {})


class AttackFSMContext:
    """Safe accessor around the FSM context dict used by AttackState."""

    def __init__(self, world: Any, entity_id: int):
        npc_state_map = world.components.get("NPCState", {})
        npc_state = npc_state_map.get(entity_id)
        self._fsm = getattr(npc_state, "fsm", None)
        self._context = getattr(self._fsm, "context", None) if self._fsm else None

    @property
    def has_context(self) -> bool:
        return isinstance(self._context, dict)

    def get_float(self, key: str, default: float) -> float:
        if not self.has_context:
            return default
        try:
            return float(self._context.get(key, default))
        except Exception:
            return default

    def get_bool(self, key: str, default: bool = False) -> bool:
        if not self.has_context:
            return default
        try:
            return bool(self._context.get(key, default))
        except Exception:
            return default

    def get(self, key: str, default: Any = None) -> Any:
        """Generic safe read from FSM context without coercion."""
        if not self.has_context:
            return default
        try:
            return self._context.get(key, default)
        except Exception:
            return default

    def get_vector(self, key: str, default: tuple[float, float] | None = None) -> tuple[float, float]:
        """Return a 2D vector stored in context under key, tolerating lists/tuples.

        Falls back to the provided default or (1.0, 0.0) if malformed.
        """
        if default is None:
            default = (1.0, 0.0)
        if not self.has_context:
            return default
        try:
            val = self._context.get(key)
            if isinstance(val, (list, tuple)) and len(val) >= 2:
                return float(val[0]), float(val[1])
        except Exception:
            pass
        return default

    def set(self, key: str, value: Any) -> None:
        if self.has_context:
            self._context[key] = value

    def pop(self, key: str, default: Any = None) -> Any:
        if not self.has_context:
            return default
        try:
            return self._context.pop(key, default)
        except Exception:
            return default

    def ensure_attack_duration(self, fallback: float) -> float:
        duration = self.get_float("attack_duration", fallback)
        if duration <= 0:
            duration = fallback
        if self.has_context:
            self._context["attack_duration"] = duration
        return duration

    def mark_attack_start(self, timestamp: float) -> float:
        start = self.get_float("attack_start", timestamp)
        if start <= 0:
            start = timestamp
        if self.has_context:
            self._context["attack_start"] = start
        return start

    def mark_attack_fired(self, timestamp: float, lock_duration: float) -> None:
        if not self.has_context:
            return
        lock_until = max(0.0, timestamp + max(0.0, lock_duration))
        self._context["lock_move_until"] = lock_until
        self._context["attack_fired"] = True

    def lock_expired(self, timestamp: float) -> bool:
        lock_until = self.get_float("lock_move_until", 0.0)
        return timestamp >= lock_until


def normalize_vector(dx: float, dy: float) -> Tuple[float, float, float]:
    magnitude = (dx * dx + dy * dy) ** 0.5
    if magnitude <= 1e-6:
        return 1.0, 0.0, 0.0
    return dx / magnitude, dy / magnitude, magnitude


def _extract_center(position: Any, sprite: Any, scale: Any) -> Vector2:
    try:
        center = compute_entity_center(position, sprite, scale)
        return float(center.x), float(center.y)
    except Exception:
        return float(getattr(position, "x", 0.0)), float(getattr(position, "y", 0.0))
