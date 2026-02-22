import pygame

from roguelike_ui.widgets.title_panel import TitlePanel


def test_title_panel_empty_text_and_large_border_radius():
    screen = pygame.Surface((200, 100), flags=pygame.SRCALPHA)
    font = pygame.font.SysFont(None, 16)

    # Empty text with default paddings ensures a non-zero background size
    tp = TitlePanel(text="", font=font, x=5, y=7, border_width=5, border_radius=64)
    tp.render(screen)

    # Sample inside the expected background area; pixel should be non-transparent
    px = screen.get_at((tp.x + 1, tp.y + 1))
    assert px.a > 0


def test_title_panel_re_render_at_new_position():
    screen = pygame.Surface((240, 120), flags=pygame.SRCALPHA)
    font = pygame.font.SysFont(None, 18)

    tp = TitlePanel(text="Edge Case Title", font=font, x=0, y=0)
    tp.render(screen)

    # Move and render again; new location should be painted as well
    tp.x, tp.y = 50, 40
    tp.render(screen)
    px = screen.get_at((tp.x + 1, tp.y + 1))
    assert px.a > 0
