from __future__ import annotations

# Default templates for stats shown in the Properties tab
# Values are None so the UI highlights them for editing.

PLAYER_STATS_TEMPLATE: dict[str, object] = {
    "max_strength": None,
    "max_intelligence": None,
    "max_dexterity": None,
    "initial_strength": None,
    "initial_intelligence": None,
    "initial_dexterity": None,
    "basic_speed": None,
    "basic_attack": None,
    "basic_armor": None,
    "attack_duration": None,
    "basic_trail": {
        "interval": None,
        "life_time": None,
        "max_trails": None,
    },
    "basic_death_timer_duration": None,
}

MONSTER_STATS_TEMPLATE: dict[str, object] = {
    "hp": None,
    "speed": None,
    "faction": None,
    "aggro_range": None,
    "melee_range": None,
    "melee_damage": None,
    "melee_cooldown": None,
    "defense": None,
    "power": None,
    "damage_duration": None,
    "chasing_speed": None,
    "feet_width_factor": None,
    "feet_height_factor": None,
    "spawn_padding": None,
    "spawn_count": None,
    "spawn_margin": None,
}
