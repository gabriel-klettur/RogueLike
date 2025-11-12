"""Monster profile helpers for attack-related behavior."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional


@dataclass(frozen=True)
class MonsterProfile:
    """Lightweight view over an NPC's archetype traits."""

    raw_type: Optional[str]
    normalized_type: str

    @property
    def is_final_boss(self) -> bool:
        """Return True when the monster belongs to the Final Boss Barbol lineage."""
        return self.normalized_type.startswith("final_boss_barbol")

    @classmethod
    def from_world(cls, world, entity_id: int) -> "MonsterProfile":
        """Build the profile from the world archetype component if available."""
        arche_map = world.components.get("MonsterArchetype", {})
        archetype = arche_map.get(entity_id)
        raw_type = getattr(archetype, "type", None)
        normalized_type = str(raw_type or "").lower()
        return cls(raw_type=raw_type, normalized_type=normalized_type)

    def resolve_spell_id(self) -> str:
        """Pick the slash spell identifier associated with the archetype."""
        if self.is_final_boss:
            return "boss_barbol_slash"

        mapping = {
            "barbol_oscuro": "hostile_slash_dark",
            "oscuro": "hostile_slash_dark",
            "dark": "hostile_slash_dark",
            "barbol_morado": "hostile_slash_purple",
            "morado": "hostile_slash_purple",
            "purple": "hostile_slash_purple",
            "barbol_boss": "hostile_slash_red",
            "boss": "hostile_slash_red",
            "barbol_cyan": "hostile_slash_cyan",
            "cyan": "hostile_slash_cyan",
            "barbol_gris": "hostile_slash_gray",
            "gris": "hostile_slash_gray",
            "gray": "hostile_slash_gray",
            "grey": "hostile_slash_gray",
            "barbol_gigante": "hostile_slash_giant",
            "gigante": "hostile_slash_giant",
            "giant": "hostile_slash_giant",
        }
        return mapping.get(self.normalized_type, "hostile_slash")
