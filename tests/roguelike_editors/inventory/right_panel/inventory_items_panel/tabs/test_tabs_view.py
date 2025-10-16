import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_view import TabsView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_tabs_rect_positions():
    font = pygame.font.SysFont(None, 24)
    button_size = (50, 20)
    margin = 5
    view = TabsView(font, button_size, margin)
    overlay = pygame.Surface((200, 200))
    grid_origin_x = 10
    grid_origin_y = 100
    show_y = grid_origin_y - button_size[1] - margin
    rects = view.draw_tabs(overlay, grid_origin_x, grid_origin_y, -1, -1, active_tab='default', slots_count=10)
    assert 'show_default' in rects and 'show_active' in rects
    assert rects['show_default'].topleft == (grid_origin_x, show_y)
    assert rects['show_active'].topleft == (grid_origin_x + button_size[0] + 10, show_y)


def test_get_slots_data_default_player():
    model = SimpleNamespace(
        editing_side='default',
        current_category='player',
        default_data={'player': {'slots': ['s1', 's2']}},
        active_data={}
    )
    slots = TabsView(None, (0, 0), 0).get_slots_data(model)
    assert slots == ['s1', 's2']


def test_get_slots_data_default_monsters():
    model = SimpleNamespace(
        editing_side='default',
        current_category='hostile',
        selected_eid=1,
        default_data={
            'monsters': {'m1': {'template_id': 1, 'inventory': [
                {'item': 'a', 'min': 2},
                {'item': 'b', 'min': 1}
            ]}}
        },
        active_data={
            'monsters': {'1': {'template_id': 1, 'slots': [
                {'item': 'a', 'quantity': 5},
                {'item': 'b', 'quantity': 3},
                {'item': 'c', 'quantity': 0}
            ]}}
        }
    )
    slots = TabsView(None, (0, 0), 0).get_slots_data(model)
    assert slots == [
        {'item': 'a', 'quantity': 2},
        {'item': 'b', 'quantity': 1},
        None
    ]


def test_get_slots_data_active():
    model = SimpleNamespace(
        editing_side='active',
        current_category='player',
        selected_eid=99,
        default_data={},
        active_data={'player': {'99': {'slots': ['p1', None]}}}
    )
    slots = TabsView(None, (0, 0), 0).get_slots_data(model)
    assert slots == ['p1', None]


def test_get_slots_data_empty_or_invalid():
    model = SimpleNamespace(
        editing_side='default',
        current_category='unknown',
        selected_eid=0,
        default_data={},
        active_data={}
    )
    slots = TabsView(None, (0, 0), 0).get_slots_data(model)
    assert slots == []
    model.editing_side = 'active'
    slots2 = TabsView(None, (0, 0), 0).get_slots_data(model)
    assert slots2 == []
