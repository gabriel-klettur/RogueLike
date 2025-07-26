import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.list.list_view import ListView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

class DummyScrollPanel:
    def __init__(self):
        self.scroll_offset = 0
        self.items_set = None

    def set_items(self, items):
        self.items_set = items

    def draw(self, surface, panel_rect):
        pass


def test_draw_returns_panel_and_list_rects():
    font = pygame.font.SysFont(None, 24)
    view = ListView(font)
    # stub scroll_panel
    dummy = DummyScrollPanel()
    view.scroll_panel = dummy
    base_rect = pygame.Rect(5, 5, 100, 100)
    items = ['x', 'y']
    surface = pygame.Surface((200, 200))
    model = SimpleNamespace(current_category='player', selected_eid=None)
    results = view.draw(surface, model, base_rect, items)
    assert 'panel_rect' in results and 'list_rect' in results
    assert results['panel_rect'] == base_rect
    assert results['list_rect'] == base_rect
    assert dummy.items_set == items


def test_draw_monsters_highlight_no_error(monkeypatch):
    font = pygame.font.SysFont(None, 24)
    view = ListView(font)
    dummy = DummyScrollPanel()
    view.scroll_panel = dummy
    base_rect = pygame.Rect(0, 0, 50, 50)
    items = ['E1', '  Slot x1']
    surface = pygame.Surface((100, 100))
    model = SimpleNamespace(current_category='monsters', selected_eid='E1')
    # stub mouse position inside panel
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (10, 10))
    # ensure scroll_offset
    view.scroll_panel.scroll_offset = 0
    results = view.draw(surface, model, base_rect, items)
    # no exceptions and results correct
    assert results['panel_rect'] == base_rect
