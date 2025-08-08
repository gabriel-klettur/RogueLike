"""
Shared mappings and helpers for asset states and directions.
"""
from typing import Dict, Iterable, Tuple

# Grid directions used by the assets grid UI
DIR_MAP: Dict[str, str] = {
    'nw': 'up_left',
    'n': 'up',
    'ne': 'up_right',
    'w': 'left',
    'e': 'right',
    'sw': 'down_left',
    's': 'down',
    'se': 'down_right',
}

# Optional center direction if needed by UI (not used in current grid)
CENTER_KEY = 'c'

_STATE_UI_ALIASES = {
    # UI shows 'chase' where data may use 'walking'
    'walking': 'chase',
}


def map_state_to_ui(state: str) -> str:
    """Map raw state names to UI-exposed names (e.g., walking -> chase)."""
    return _STATE_UI_ALIASES.get(state, state)


def iter_grid_dir_keys() -> Iterable[str]:
    """Return the canonical order of grid direction keys for rendering."""
    return ('nw', 'n', 'ne', 'w', 'e', 'sw', 's', 'se')


def sheet_path_to_grid(sheet_path: str) -> Dict[str, str]:
    """
    Expand a single sprite-sheet path to all grid directions.
    Currently the same sheet is used for all directions in the UI.
    """
    return {k: sheet_path for k in DIR_MAP.keys()}
