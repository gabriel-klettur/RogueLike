import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.tabs.tabs_view import TabsView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_returns_tab_rects():
    font = pygame.font.SysFont(None, 24)
    view = TabsView(font)
    surface = pygame.Surface((100, 50))
    default_rect = pygame.Rect(0, 0, 30, 20)
    ground_rect = pygame.Rect(30, 0, 30, 20)
    result = view.draw(surface, 'default', default_rect, ground_rect)
    assert isinstance(result, dict)
    assert 'tab_rects' in result
    assert result['tab_rects'] == [default_rect, ground_rect]


def test_draw_switch_tab_changes_nothing_but_return():
    font = pygame.font.SysFont(None, 24)
    view = TabsView(font)
    surface = pygame.Surface((60, 20))
    default_rect = pygame.Rect(0, 0, 20, 20)
    ground_rect = pygame.Rect(20, 0, 20, 20)
    # draw with ground selected
    res = view.draw(surface, 'ground', default_rect, ground_rect)
    assert res == {'tab_rects': [default_rect, ground_rect]}
