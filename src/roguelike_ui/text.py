import pygame
from pathlib import Path

def ellipsize(text, font, max_width, ellipsis="..."):
    """Truncate text with ellipsis to fit max_width using font metrics."""
    if font.size(text)[0] <= max_width:
        return text
    for i in range(len(text), 0, -1):
        candidate = text[:i] + ellipsis
        if font.size(candidate)[0] <= max_width:
            return candidate
    return ellipsis


def render_centered(surface, text, font, color, rect):
    """Render text centered in rect."""
    surf = font.render(text, True, color)
    pos = surf.get_rect(center=rect.center)
    surface.blit(surf, pos)
