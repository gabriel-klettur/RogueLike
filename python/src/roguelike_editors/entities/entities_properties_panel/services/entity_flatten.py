from typing import Dict, Any

from .assets_maps import DIR_MAP, map_state_to_ui


def flatten_entity_data(player_stats: Dict[str, Any],
                        player_assets: Dict[str, Any],
                        monsters: Dict[str, Any],
                        ent_id: str) -> Dict[str, Any]:
    """
    Produce a flat dictionary of properties and asset entries suitable for the
    Properties Panel view, independent of the original JSON nesting.
    Keys for assets follow the convention: 'asset_{state}_{dirKey}'.
    """
    if not ent_id:
        return {}

    if ent_id in player_stats:
        return _flatten_player_entity(ent_id, player_stats, player_assets)

    if ent_id in monsters:
        return _flatten_monster_entity(ent_id, monsters)

    return {}


def _flatten_player_entity(ent_id: str,
                           player_stats: Dict[str, Any],
                           player_assets: Dict[str, Any]) -> Dict[str, Any]:
    stats = player_stats.get(ent_id, {})
    assets = player_assets.get(ent_id, {})

    merged: Dict[str, Any] = dict(stats)
    merged['id'] = ent_id
    # Active set defaults to 'sets' for players
    merged['active_set'] = assets.get('active_set', 'sets')

    # Expand no-sets first
    no_sets = assets.get('no-sets', {})
    for state, dirs in no_sets.items():
        ui_state = map_state_to_ui(state)
        for dir_key, path in dirs.items():
            merged[f'asset_{ui_state}_{dir_key}'] = path

    # Expand sets (sprite-sheet) overriding direction entries
    sets = assets.get('sets', {}).get('sprites_set', {})
    for state, paths in sets.items():
        if not paths:
            continue
        sheet_path = paths[0]
        ui_state = map_state_to_ui(state)
        for dir_key in DIR_MAP.keys():
            merged[f'asset_{ui_state}_{dir_key}'] = sheet_path

    return merged


def _flatten_monster_entity(ent_id: str, monsters: Dict[str, Any]) -> Dict[str, Any]:
    monster = monsters.get(ent_id, {})
    stats = monster.get('stats', {})
    assets_def = monster.get('assets', {})

    merged: Dict[str, Any] = dict(stats)
    merged['id'] = ent_id

    # Active set defaults to 'no-sets' for monsters
    active_set = assets_def.get('active_set', 'no-sets')
    merged['active_set'] = active_set

    # Flatten no-sets (individual directions)
    no_sets = assets_def.get('no-sets', {})
    for state, dirs in no_sets.items():
        if state == 'sprites_data_no-set':
            continue
        for dir_key, path in dirs.items():
            merged[f"asset_{state}_{dir_key}"] = path

    # Flatten sets (sprite-sheet) overriding per-direction
    sets_group = assets_def.get('sets', {}).get('sprites_set', {})
    for state, paths in sets_group.items():
        if not paths:
            continue
        sheet_path = paths[0]
        for dir_key in DIR_MAP.keys():
            merged[f"asset_{state}_{dir_key}"] = sheet_path

    # Metadata (scale, tint, etc.)
    data_no = assets_def.get('no-sets', {}).get('sprites_data_no-set', {})
    data_set = assets_def.get('sets', {}).get('sprites_data_set', {})

    for key, value in data_no.items():
        merged[f'no-set_{key}'] = value
    for key, value in data_set.items():
        merged[f'set_{key}'] = value

    merged['tint'] = (data_no.get('tint') if active_set == 'no-sets' else data_set.get('tint'))

    return merged
