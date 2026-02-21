"""HUD configuration constants: sizes, colors, paddings.

This module centralizes tunables for HUD widgets. Values are conservative defaults
and can be adjusted without touching logic code.
"""
from __future__ import annotations

from typing import Tuple

# Layout
GRID_ROWS: int = 3
GRID_COLS: int = 10
GRID_CELL_SIZE: Tuple[int, int] = (48, 48)
GRID_MARGIN: int = 8
GRID_PADDING: int = 6
GRID_PAGE_KEYS: Tuple[str, str] = ("K_PAGEUP", "K_PAGEDOWN")
GRID_BOTTOM_MARGIN: int = 48  # Extra distance from bottom to avoid XP bar overlap
MINIMIZE_BUTTON_SIZE: Tuple[int, int] = (24, 18)
MINIMIZED_BOX_SIZE: Tuple[int, int] = (80, 26)

# Colors (RGBA where applicable)
COLOR_BG: Tuple[int, int, int, int] = (20, 20, 28, 200)
COLOR_BORDER: Tuple[int, int, int, int] = (180, 180, 200, 255)
COLOR_TEXT: Tuple[int, int, int, int] = (235, 235, 240, 255)
COLOR_HOVER: Tuple[int, int, int, int] = (50, 50, 70, 230)
COLOR_PRESSED: Tuple[int, int, int, int] = (70, 70, 90, 240)
COLOR_PAGING_HOVER: Tuple[int, int, int, int] = (255, 230, 80, 160)

# Z-layer for UI; should align with engine config_z_layer (ui=10)
Z_UI: int = 10
