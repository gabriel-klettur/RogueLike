from dataclasses import dataclass
import pygame


@dataclass(frozen=True)
class Fonts:
    """Font bundle to keep UI consistent."""
    large: pygame.font.Font
    medium: pygame.font.Font
    small: pygame.font.Font
    dropdown: pygame.font.Font


def make_fonts(base_size: int = 16) -> Fonts:
    """Create a standard set of fonts for the editor UI."""
    pygame.font.init()
    return Fonts(
        large=pygame.font.SysFont(None, base_size * 3),
        medium=pygame.font.SysFont(None, 24),
        small=pygame.font.SysFont(None, 20),
        dropdown=pygame.font.SysFont("Arial", 14),
    )
