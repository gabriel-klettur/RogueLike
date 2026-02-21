import pygame
from types import SimpleNamespace
import pytest

from roguelike_editors.inventory.left_panel.tabs.tabs_view import TabsView

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

def test_draw_returns_tab_rects_and_categories():
    font = pygame.font.SysFont(None, 24)
    view = TabsView(font, margin=5)
    surface = pygame.Surface((200, 200))
    model = SimpleNamespace(categories=['a', 'b'], current_category='b')
    rects = view.draw(surface, model)
    assert isinstance(rects, list)
    assert len(rects) == 2
    for rect, cat in rects:
        assert isinstance(rect, pygame.Rect)
    assert [cat for _, cat in rects] == ['a', 'b']
    assert view.tab_rects == rects


def test_rect_positions_update_with_each_draw():
    font = pygame.font.SysFont(None, 24)
    view = TabsView(font)
    surface = pygame.Surface((200,200))
    model = SimpleNamespace(categories=['x'], current_category='x')
    rects1 = view.draw(surface, model)
    rects2 = view.draw(surface, model)
    assert rects1 != []
    assert rects1[0] == rects2[0]
