from __future__ import annotations
import pygame
from roguelike_editors.entities.services.constants import UI_MARGIN


def open_new_monster_properties(editor: "EntitiesEditorController") -> None:
    """Crea una nueva definición de monstruo temporal y abre el panel de propiedades."""
    if editor.model.spawn_mode_active:
        editor.exit_spawn_mode()
    if editor.model.delete_mode_active:
        editor.exit_delete_mode()

    base = 'new_monster'
    new_id = base
    idx = 2
    while new_id in editor.model.monsters or new_id in editor.model.player_stats:
        new_id = f"{base}_{idx}"
        idx += 1

    directions = ['s', 'se', 'e', 'ne', 'n', 'nw', 'w', 'sw']
    states = ['idle', 'walk', 'chase', 'cast', 'attack', 'damage', 'death']

    def empty_dirs():
        return {d: None for d in directions}

    no_sets = {st: empty_dirs() for st in states}
    no_sets['sprites_data_no-set'] = {
        'scale_idle': 0.5,
        'scale_walk': 0.5,
        'scale_chase': 0.5,
        'scale_cast': 0.5,
        'scale_attack': 0.5,
        'scale_damage': 0.5,
        'scale_death': 0.55,
        'tint': None,
    }
    sets = {
        'sprites_set': {st: [] for st in states},
        'sprites_data_set': {
            'scale_idle': 0.5,
            'scale_walk': 0.5,
            'scale_chase': 0.5,
            'scale_cast': 0.5,
            'scale_attack': 0.5,
            'scale_damage': 0.5,
            'scale_death': 0.55,
            'tint': None,
        }
    }
    default_stats = {
        'hp': 1,
        'speed': 1.0,
        'faction': 'EVIL',
        'aggro_range': 10,
        'melee_range': 5,
        'melee_damage': 1,
        'melee_cooldown': 1.0,
        'defense': 1,
        'power': 1,
        'damage_duration': 0.5,
        'chasing_speed': 1.0,
        'feet_width_factor': 0.5,
        'feet_height_factor': 0.5,
        'spawn_padding': 5,
        'spawn_count': 10,
        'spawn_margin': 0,
    }
    editor.model.monsters[new_id] = {
        '__pending__': True,
        'stats': default_stats,
        'assets': {
            'active_set': 'no-sets',
            'sets': sets,
            'no-sets': no_sets,
        }
    }

    editor.properties_controller.model.hovered_entity_id = None
    editor.properties_controller.model.selected_id = new_id
    editor.picker_controller.model.blink = False
    try:
        editor.render(editor.game.screen)
    except Exception:
        pass
