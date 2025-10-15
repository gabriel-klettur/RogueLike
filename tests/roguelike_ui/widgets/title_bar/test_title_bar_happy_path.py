import pygame

from roguelike_ui.widgets.title_bar import TitleBar
from roguelike_ui.ui_blocker import clear_blockers, is_blocked


def test_title_bar_renders_and_blocks_ui_underneath():
    clear_blockers()
    screen = pygame.Surface((300, 120), flags=pygame.SRCALPHA)

    tb = TitleBar(text="Inventory", x=20, y=15, font=pygame.font.SysFont(None, 18))
    rect = tb.render(screen)

    # Returned rect must be within screen and non-empty
    assert rect.width > 0 and rect.height > 0
    assert 0 <= rect.x < screen.get_width()
    assert 0 <= rect.y < screen.get_height()

    # UI blocker should mark points inside rect as blocked
    cx = rect.x + rect.width // 2
    cy = rect.y + rect.height // 2
    assert is_blocked(cx, cy) is True
