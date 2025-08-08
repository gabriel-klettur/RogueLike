"""
Helpers for building asset keys and resolving asset paths for the Assets Grid.
"""
from typing import Optional

from .assets_constants import SUBTAB_NO_SET
from .assets_maps import ui_state_to_nosets_json


def build_asset_key(ui_state: str, dir_key: str) -> str:
    """Return the canonical entity property key used in UI data.
    Example: ('idle', 'nw') -> 'asset_idle_nw'
    """
    return f"asset_{ui_state}_{dir_key}"


def resolve_asset_path(
    entity_id: Optional[int],
    parent_model,
    entity_data: dict,
    ui_state: str,
    dir_key: str,
    active_sub_tab: str,
) -> Optional[str]:
    """Resolve the displayable path for a grid cell based on the current sub-tab.

    - For SUBTAB_NO_SET and player entities, read from model.player_assets[entity]['no-sets']
      using the json-state name (e.g., 'chase' -> 'walking').
    - Otherwise, read from the entity's own properties using the UI key 'asset_{state}_{dir}'.
    """
    if (
        active_sub_tab == SUBTAB_NO_SET
        and entity_id is not None
        and hasattr(parent_model, 'player_stats')
        and entity_id in parent_model.player_stats
    ):
        player_assets = getattr(parent_model, 'player_assets', {}).get(entity_id, {})
        no_sets = player_assets.get('no-sets', {})
        json_state = ui_state_to_nosets_json(ui_state)
        dirs = no_sets.get(json_state, {})
        return dirs.get(dir_key)

    key = build_asset_key(ui_state, dir_key)
    return entity_data.get(key)
