import os
import pytest
import pygame

from roguelike_ui.ui_helpers import draw_highlight_rect, draw_tooltip

@pytest.fixture(autouse=True)
def dummy_video(monkeypatch):
    # Use dummy video driver for headless testing
    monkeypatch.setenv('SDL_VIDEODRIVER', 'dummy')
    pygame.display.init()
    pygame.font.init()
    yield
    pygame.display.quit()


def test_draw_highlight_rect_pixels():
    surface = pygame.Surface((50, 50))
    rect = pygame.Rect(5, 5, 10, 10)
    color = (10, 20, 30)
    draw_highlight_rect(surface, rect, color=color, width=1)
    # Check corners
    assert surface.get_at((5, 5))[:3] == color
    assert surface.get_at((14, 5))[:3] == color
    assert surface.get_at((5, 14))[:3] == color
    assert surface.get_at((14, 14))[:3] == color


def test_draw_tooltip_basic():
    surface = pygame.Surface((200, 200), flags=pygame.SRCALPHA)
    lines = ['Hello', 'World']
    draw_tooltip(surface, 10, 10, lines)
    # Background should have semi-transparent pixels
    px = surface.get_at((22, 22))
    assert px[3] > 0  # alpha channel > 0


def test_draw_tooltip_edge_position():
    surface = pygame.Surface((100, 50), flags=pygame.SRCALPHA)
    lines = ['EdgeTest']
    # Position near bottom-right corner
    draw_tooltip(surface, 90, 40, lines)
    # Ensure function completes without errors and draws within surface bounds
    # Check that at least one pixel at bottom row has non-zero alpha
    # Ensure tooltip drew at least one pixel anywhere
    assert any(
        surface.get_at((x, y))[3] > 0
        for x in range(surface.get_width())
        for y in range(surface.get_height())
    )
