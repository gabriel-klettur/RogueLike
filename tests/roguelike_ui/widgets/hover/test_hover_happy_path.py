import pygame

from roguelike_ui.widgets.hover import draw_hover, draw_selection_border


def test_draw_hover_and_selection_border():
    surf = pygame.Surface((20, 20), flags=pygame.SRCALPHA)
    rect = pygame.Rect(2, 3, 10, 8)

    draw_hover(surf, rect)
    draw_selection_border(surf, rect, color=(255, 0, 0), thickness=1)

    assert surf.get_size() == (20, 20)
