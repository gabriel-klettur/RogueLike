"""Default input bindings and settings for the game.

This module centralizes the default mapping to keep the main InputConfig
implementation concise and maintainable.
"""
from __future__ import annotations

# Default keyboard bindings stored as string names (persisted to JSON)
# Use K_* for keyboard and plain names when pygame.key.key_code can resolve.
DEFAULT_BINDINGS: dict[str, str] = {
    # Movement
    "move_up": "K_UP",
    "move_down": "K_DOWN",
    "move_left": "K_LEFT",
    "move_right": "K_RIGHT",
    # Spells (keyboard)
    "spell_lightball": "K_q",
    "spell_slash": "K_e",
    "spell_healing_aura": "K_x",
    "spell_darkball": "K_1",
    "spell_iceball": "K_2",
    "spell_arcane_flame": "K_c",
    "spell_firework_launch": "K_v",
    "spell_smoke": "K_f",
    "spell_smoke_emitter": "K_g",
    "spell_sphere_magic_shield": "K_t",
    "spell_teleport": "K_j",
    "spell_lightning": "K_r",
    "spell_boomerang": "K_6",
    "spell_chain_lightning": "K_7",
    "spell_vortex_pull": "K_8",
    "spell_vortex_push": "K_9",
    # Game
    "pause": "K_ESCAPE",
    "toggle_inventory": "K_i",
    "interact": "K_RETURN",
    "select_class": "K_F2",
    # Editors toggles
    "toggle_particles_editor": "K_F1",
    "toggle_spawner_editor": "K_F3",
    "toggle_spells_editor": "K_F4",
    "toggle_entities_editor": "K_F5",
    "toggle_inventory_editor": "K_F6",
    "toggle_item_editor": "K_F7",
    "toggle_tile_editor": "K_F8",
    "toggle_debug_overlay": "K_F9",
    "toggle_building_editor": "K_F10",
    "toggle_map_editor": "K_F11",
    "toggle_fsm_editor": "K_F12",
    # Dev hot-reload (F1 without modifiers)
    "reload_data": "K_F1",
}

# Default mouse bindings (M_* names) for core actions
MOUSE_DEFAULTS: dict[str, str] = {
    "mouse_fireball": "M_LEFT",
    "mouse_laser_beam": "M_MIDDLE",
    "mouse_dash": "M_RIGHT",
}

# Tri-slot bases for keyboard slots (A/B). Each base gets kb_<base>_a and kb_<base>_b
TRISLOT_BASES: tuple[str, ...] = (
    "fireball",
    "laser_beam",
    "dash",
)
