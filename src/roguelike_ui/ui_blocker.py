"""
Generic UI blocker for suppressing hover/effects under panels.
"""
import pygame

# Registered panel rects
_panel_rects: list[pygame.Rect] = []

def clear_blockers() -> None:
    """Clear all registered panel blockers."""
    _panel_rects.clear()

def register_blocker(rect: pygame.Rect) -> None:
    """Register a panel rect to block underlying hover/effects."""
    _panel_rects.append(rect)

def is_blocked(x: int, y: int) -> bool:
    """Return True if point (x,y) is within any registered panel rect."""
    for rect in _panel_rects:
        if rect.collidepoint(x, y):
            return True
    return False
