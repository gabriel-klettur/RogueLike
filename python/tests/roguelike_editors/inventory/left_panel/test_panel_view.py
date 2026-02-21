import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.panel_view import PanelView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

def test_draw_delegates_and_combines(monkeypatch):
    font = pygame.font.SysFont(None, 24)
    view = PanelView(font, margin=5)
    # stub tabs_view.draw and list_view.draw
    dummy_tabs_output = [('rect', 'cat')]
    monkeypatch.setattr(view, 'tabs_view', SimpleNamespace(draw=lambda surface, model: dummy_tabs_output))
    dummy_list_results = {'panel_rect': pygame.Rect(1, 2, 3, 4), 'list_rect': pygame.Rect(5, 6, 7, 8)}
    monkeypatch.setattr(view, 'list_view', SimpleNamespace(draw=lambda surface, model, base_rect, items: dummy_list_results))
    surface = pygame.Surface((100, 100))
    model = SimpleNamespace()
    base_rect = pygame.Rect(10, 10, 50, 50)
    items = ['i1', 'i2']
    results = view.draw(surface, model, base_rect, items)
    assert results['tab_rects'] == dummy_tabs_output
    assert results['panel_rect'] == dummy_list_results['panel_rect']
    assert results['list_rect'] == dummy_list_results['list_rect']
    assert view.tab_rects == dummy_tabs_output
    assert view.panel_rect == dummy_list_results['panel_rect']
