import pygame

from roguelike_ui.widgets.title_bar import TitleBar


def test_title_bar_empty_text_and_reposition():
    screen = pygame.Surface((200, 80), flags=pygame.SRCALPHA)

    tb = TitleBar(text="", x=0, y=0, font=pygame.font.SysFont(None, 12))
    rect1 = tb.render(screen)
    # Empty text still yields a minimal rect (padding only)
    assert rect1.width > 0 and rect1.height > 0

    # Move title bar and change text to a long string
    tb.set_pos(50, 10)
    tb.update_text("X" * 120)
    rect2 = tb.render(screen)

    # Position updated, width should grow for long text
    assert rect2.x == 50 and rect2.y == 10
    assert rect2.width >= rect1.width
