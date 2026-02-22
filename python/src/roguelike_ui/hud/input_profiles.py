from __future__ import annotations

from typing import List


class InputProfileProvider:
    """Minimal provider for action profiles per mode.

    In this initial version, modes map to predefined action lists. Later, this
    can be data-driven and synchronized with game/editor states.
    """

    _DEFAULT_ACTIONS: List[str] = [
        # Movement & interaction
        "move_up", "move_down", "move_left", "move_right",
        "attack", "interact", "dash", "toggle_minimap",
        # Sample spells & toggles
        "spell_lightball", "spell_fireball", "spell_ice", "spell_storm",
        "toggle_inventory", "toggle_buildings_editor", "toggle_tiles_editor",
        "reload_data",
    ]

    def get_mode(self, world=None, state=None) -> str:
        # Minimal: always gameplay for now
        return "gameplay"

    def get_actions_for_mode(self, mode: str) -> List[str]:
        return list(self._DEFAULT_ACTIONS)
