from __future__ import annotations

import pygame


def wrap_text(font: pygame.font.Font, text: str, max_width: int) -> list[str]:
    """Wrap a string to multiple lines constrained by max_width in pixels.

    Args:
        font: Pygame font used to measure text.
        text: Input string.
        max_width: Maximum width allowed in pixels.

    Returns:
        List of lines after wrapping.
    """
    words = text.split(" ")
    lines: list[str] = []
    current = ""
    for w in words:
        test = current + (" " if current else "") + w
        if font.size(test)[0] <= max_width:
            current = test
        else:
            if current:
                lines.append(current)
            current = w
    if current:
        lines.append(current)
    return lines


def truncate_text(font: pygame.font.Font, text: str, max_width: int) -> str:
    """Truncate a string adding ellipsis so it fits in max_width.

    Args:
        font: Pygame font used to measure text.
        text: Input string.
        max_width: Maximum width allowed in pixels.

    Returns:
        A possibly truncated string with ellipsis if needed.
    """
    if font.size(text)[0] <= max_width:
        return text
    trimmed = text.rstrip()
    while trimmed and font.size(trimmed + "...")[0] > max_width:
        trimmed = trimmed[:-1]
    return trimmed + "..."
