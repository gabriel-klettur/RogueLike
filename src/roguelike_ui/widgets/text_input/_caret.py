import pygame
from pygame.time import get_ticks


def caret_on(blink_interval: int) -> bool:
    """Return True when the caret should be visible for a blink interval."""
    t = get_ticks()
    return (t % blink_interval) < (blink_interval // 2)
