"""
Shared mappings and helpers for asset states and directions.
"""
from typing import Dict, Iterable, Tuple, Optional

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

# Canonical 3x3 order (center None) used by the grid view
GRID_ORDER_3X3: Tuple[Optional[str], ...] = (
    'nw', 'n', 'ne',
    'w',  None, 'e',
    'sw', 's', 'se',
)

# UI <-> internal engine state mappings
# UI shows 'chase' while the animator uses 'walk'
UI_TO_INTERNAL_STATE: Dict[str, str] = {
    'chase': 'walk',
}
INTERNAL_TO_UI_STATE: Dict[str, str] = {
    'walk': 'chase',
}

# UI -> JSON (no-sets) state mappings for player assets
# Player JSON uses 'walk' (not 'walking') for the walking state
UI_TO_NOSETS_JSON_STATE: Dict[str, str] = {
    'chase': 'walk',
}


def ui_state_to_internal(state: str) -> str:
    """Map UI state (e.g., 'chase') to internal animator state (e.g., 'walk')."""
    return UI_TO_INTERNAL_STATE.get(state, state)


def internal_state_to_ui(state: str) -> str:
    """Map internal animator state (e.g., 'walk') to UI state (e.g., 'chase')."""
    return INTERNAL_TO_UI_STATE.get(state, state)


def ui_state_to_nosets_json(state: str) -> str:
    """Map UI state (e.g., 'chase') to player 'no-sets' JSON state (e.g., 'walking')."""
    return UI_TO_NOSETS_JSON_STATE.get(state, state)


def map_state_to_ui(state: str) -> str:
    """Map raw state names to UI-exposed names (e.g., walking -> chase)."""
    # Back-compat helper kept for existing callers
    return internal_state_to_ui(state) if state in INTERNAL_TO_UI_STATE else state


def iter_grid_dir_keys() -> Iterable[str]:
    """Return the canonical order of grid direction keys for rendering."""
    return ('nw', 'n', 'ne', 'w', 'e', 'sw', 's', 'se')


def sheet_path_to_grid(sheet_path: str) -> Dict[str, str]:
    """
    Expand a single sprite-sheet path to all grid directions.
    Currently the same sheet is used for all directions in the UI.
    """
    return {k: sheet_path for k in DIR_MAP.keys()}
