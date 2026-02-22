import pytest
import pygame
from roguelike_ui.widgets.scroll_panel import ScrollPanel
from roguelike_editors.inventory.right_panel.item_selection_panel.list.list_view import ListView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view():
    font = pygame.font.SysFont(None, 24)
    margin = 3
    view = ListView(font, margin)
    # stub scroll_panel to record items
    panel = ScrollPanel(font, margin=margin)
    panel.items = ['x', 'y', 'z']
    panel.scroll_offset = 5
    view.scroll_panel = panel
    return view


def test_draw_hover_and_selection_highlights(tmp_path, setup_view):
    view = setup_view
    surface = pygame.Surface((200, 200))
    # simulate mouse position inside second item
    pygame.mouse.set_pos((10, 40))
    rect = pygame.Rect(0, 0, 100, 100)
    # current_tab default 'inventory', selected_item scenario
    result = view.draw(surface, ['a', 'b'], rect, line_h=10, current_tab='inventory', selected_item='b', selected_index=None)
    # expect no errors and dict returned
    assert result == {}


def test_draw_ground_selection(tmp_path, setup_view):
    view = setup_view
    surface = pygame.Surface((100, 100))
    pygame.mouse.set_pos((10, 20))
    rect = pygame.Rect(0, 0, 50, 50)
    # selected_index scenario
    res = view.draw(surface, ['g1', 'g2'], rect, line_h=10, current_tab='ground', selected_item=None, selected_index=1)
    assert res == {}
