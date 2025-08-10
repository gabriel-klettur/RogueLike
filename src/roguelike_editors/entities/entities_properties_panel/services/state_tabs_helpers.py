"""Helpers for Entities State Tabs UI logic.

These helpers keep UI logic reusable and testable outside of Pygame drawing code.
"""
from typing import Dict, Optional, Tuple, Iterable
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


def format_tab_label(label: str) -> str:
    """Return a user-facing label for a tab.

    Use title case for consistency across UI components.
    """
    return label.title()


def build_tab_rects(
    labels: Iterable[str],
    font: pygame.font.Font,
    origin: Tuple[int, int],
    padding: Tuple[int, int] = (10, 5),
) -> Dict[str, pygame.Rect]:
    """Build a mapping of label -> pygame.Rect laid out horizontally.

    Parameters
    ----------
    labels: iterable of str
        Tab identifiers in the order to render.
    font: pygame.font.Font
        Font used to measure text.
    origin: (x, y)
        Top-left starting position for the first tab.
    padding: (pad_x, pad_y)
        Horizontal and vertical padding around text inside each tab.

    Returns
    -------
    Dict[str, pygame.Rect]
        Mapping of the provided labels to their computed rectangles.
    """
    x_cursor, y = origin
    pad_x, pad_y = padding
    rects: Dict[str, pygame.Rect] = {}
    for label in labels:
        text = format_tab_label(label)
        text_w, text_h = font.size(text)
        w = text_w + pad_x * 2
        h = text_h + pad_y * 2
        rect = pygame.Rect(x_cursor, y, w, h)
        rects[label] = rect
        x_cursor += w
    return rects
