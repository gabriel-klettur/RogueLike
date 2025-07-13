import pygame
import pytest
from roguelike_ui.widgets.menu_renderer import MenuRenderer

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_draw_returns_rect_centered():
    screen = pygame.Surface((800, 600))
    renderer = MenuRenderer(font_size=20)
    options = ["Option1", "Option2", "Option3"]
    selected = 1
    rect = renderer.draw(screen, selected, options)
    assert isinstance(rect, pygame.Rect)
    expected_x = (800 - renderer.surface.get_width()) // 2
    expected_y = (600 - renderer.surface.get_height()) // 2
    assert rect.topleft == (expected_x, expected_y)
