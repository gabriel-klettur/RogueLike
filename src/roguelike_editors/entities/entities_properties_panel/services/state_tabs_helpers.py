"""Helpers for Entities State Tabs UI logic.

These helpers keep UI logic reusable and testable outside of Pygame drawing code.
"""
from typing import Dict, Optional, Tuple
import pygame


def hit_test_state_tab(state_tab_rects: Dict[str, pygame.Rect], pos: Tuple[int, int]) -> Optional[str]:
    """Return the tab label at mouse position or None if none is hit.

    Parameters
    ----------
    state_tab_rects: Dict[str, pygame.Rect]
        Mapping from state label to its on-screen rectangle.
    pos: Tuple[int, int]
        Mouse position (x, y).
    """
    mx, my = pos
    for label, rect in state_tab_rects.items():
        if rect.collidepoint(mx, my):
            return label
    return None
