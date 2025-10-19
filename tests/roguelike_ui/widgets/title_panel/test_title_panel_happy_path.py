import pygame

from roguelike_ui.widgets.title_panel import TitlePanel


def test_title_panel_renders_with_default_params():
    screen = pygame.Surface((200, 100), flags=pygame.SRCALPHA)
    font = pygame.font.SysFont(None, 18)

    tp = TitlePanel(text="Hello", font=font, x=10, y=5)
    # Should render without exceptions
    tp.render(screen)

    # Basic sanity: drawing area should remain same size
    assert screen.get_size() == (200, 100)
