"""Helper objects shared across spell release handlers.

This module encapsulates the data (*spell release context*) that every
spell-type specific handler needs. It keeps lookups and cross-cutting logic
centralized so concrete handlers can stay focused.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any, MutableMapping, Tuple

import logging

from roguelike_game.config.spells_config import SPELLS

logger = logging.getLogger(__name__)


@dataclass
class SpellReleaseContext:
    """Snapshot of the information required to release a spell."""

    entity: Any
    fsm: Any

    def __post_init__(self) -> None:
        self._context: MutableMapping[str, Any] = getattr(self.fsm, "context", {})
        self.spell_key: str = str(self._context.get("spell", ""))
        self.spell_cfg: MutableMapping[str, Any] = SPELLS.get(self.spell_key, {})

    @property
    def context(self) -> MutableMapping[str, Any]:
        """Return the mutable FSM scratchpad."""

        return self._context

    @property
    def world(self) -> Any:
        """Return the ECS world associated with the caster."""

        return getattr(self.entity, "world", None)

    @property
    def spell_type(self) -> str:
        """Return the configured spell type string (empty when missing)."""

        return str(self.spell_cfg.get("type", ""))

    @property
    def camera(self) -> Any:
        """Return the rendering camera, if any."""

        return self.context.get("camera")

    def set_spawn_position(self, position: Tuple[float, float]) -> None:
        """Persist the spawn position inside the FSM context."""

        self.context["spawn_pos"] = (float(position[0]), float(position[1]))

    def get_spawn_position(self, default: Tuple[float, float] | None = None) -> Tuple[float, float]:
        """Fetch the current spawn position stored in the FSM context."""

        if "spawn_pos" not in self.context:
            if default is not None:
                self.set_spawn_position(default)
            else:
                return 0.0, 0.0
        return tuple(self.context.get("spawn_pos", default or (0.0, 0.0)))  # type: ignore[return-value]

    def get_component_map(self, component_name: str) -> MutableMapping[Any, Any]:
        """Return the component storage dictionary, creating it as needed."""

        world = self.world
        if world is None:
            return {}
        components = getattr(world, "components", {})
        return components.setdefault(component_name, {})

    def cfg_value(self, key: str, default: Any | None = None) -> Any:
        """Fetch a value from the spell configuration (dict or object)."""

        cfg = self.spell_cfg
        if hasattr(cfg, key):
            return getattr(cfg, key)
        if isinstance(cfg, MutableMapping):
            return cfg.get(key, default)
        try:
            return cfg[key]
        except Exception:  # pragma: no cover - keep parity with legacy error handling
            return default

    def mark_fireball_id(self, entity_id: Any) -> None:
        """Expose the spawned projectile identifier to the FSM context."""

        self.context["fireball_id"] = entity_id

    def deduct_mana_cost(self) -> None:
        """Charge mana to the caster unless running in god mode."""

        world = self.world
        if world is None:
            return

        try:
            if self._is_god_mode(world):
                return
            if bool(self.context.get("__mana_charged__", False)):
                return
            mana_cost = float(
                getattr(self.spell_cfg, "mana_cost", self.spell_cfg.get("mana_cost", 0.0)) or 0.0
            )
        except Exception:  # pragma: no cover - defensive parity with legacy code
            return

        if mana_cost <= 0.0:
            return

        try:
            mana_components = world.components.get("Mana", {})  # type: ignore[attr-defined]
            mana_component = mana_components.get(self.entity.id)
        except Exception:  # pragma: no cover
            return

        if mana_component is None:
            return

        try:
            current = float(getattr(mana_component, "current_mana", 0.0))
        except Exception:
            return

        new_value = int(max(0.0, current - mana_cost))
        try:
            mana_component.current_mana = new_value
        except Exception:  # pragma: no cover
            logger.debug("Failed to deduct mana for spell %s", self.spell_key, exc_info=True)

    def _is_god_mode(self, world: Any) -> bool:
        """Determine whether the caster should ignore mana consumption."""

        try:
            state = getattr(world, "state", None)
            player_entity = getattr(world, "player_entity", None)
            is_player = bool(self.entity.id == player_entity)
            godmode = bool(getattr(state, "godmode", False)) and is_player
            return godmode
        except Exception:  # pragma: no cover
            return False


def build_context(entity: Any, fsm: Any) -> SpellReleaseContext:
    """Factory helper for concise call sites."""

    return SpellReleaseContext(entity=entity, fsm=fsm)
