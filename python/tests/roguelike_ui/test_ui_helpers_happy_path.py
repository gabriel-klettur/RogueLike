import pygame

from roguelike_ui.ui_helpers import draw_highlight_rect, draw_tooltip


def test_draw_highlight_and_tooltip_do_not_crash():
    # Surface 100x100
    screen = pygame.Surface((100, 100), flags=pygame.SRCALPHA)
    rect = pygame.Rect(10, 10, 50, 40)

    draw_highlight_rect(screen, rect)

    # Tooltip near top-left within bounds
    draw_tooltip(screen, x=5, y=5, lines=["Hello", "World"])  # uses default font

    # If we reached here, calls succeeded without raising
    assert screen.get_width() == 100 and screen.get_height() == 100
