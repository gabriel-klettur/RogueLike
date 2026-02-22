import pygame

from roguelike_ui.widgets.hover import draw_hover, draw_selection_border


def test_hover_edge_cases_zero_size_and_thickness_zero():
    surf = pygame.Surface((10, 10), flags=pygame.SRCALPHA)

    # Zero-size rect should not crash
    rect_zero = pygame.Rect(0, 0, 0, 0)
    draw_hover(surf, rect_zero)

    # Thickness 0 should be accepted by pygame.draw.rect (no border drawn)
    rect = pygame.Rect(1, 1, 5, 5)
    draw_selection_border(surf, rect, color=(0, 255, 0), thickness=0)

    assert surf.get_size() == (10, 10)
