import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.inventory_items_panel.inventory_items_panel_view import InventoryItemsPanelView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view():
    font = pygame.font.SysFont(None, 24)
    slot_size = 10
    margin = 2
    button_size = (20, 10)
    get_item_image = lambda x: None
    images = {}
    errors = []
    logger = SimpleNamespace(error=lambda msg: errors.append(msg))
    view = InventoryItemsPanelView(font, slot_size, margin, button_size, get_item_image, images, logger)
    return view, errors

def test_get_slot_index_delegates(setup_view):
    view, _ = setup_view
    # stub grid_view.get_slot_index
    view.grid_view = SimpleNamespace(get_slot_index=lambda pos, gx, gy, count: 42)
    panel_rect = pygame.Rect(1, 2, 3, 4)
    idx = view.get_slot_index((5, 6), panel_rect, 10)
    assert idx == 42

def test_draw_updates_rects_and_returns_keys(setup_view):
    view, errors = setup_view
    # stub tabs_view.get_slots_data to return two slots
    view.tabs_view.get_slots_data = lambda model: [None, {'item': 'x', 'quantity': 1}]
    # use real tabs_view.draw_tabs, add/delete/save will use subviews
    overlay = pygame.Surface((200, 200))
    # model stub
    grid_model = SimpleNamespace(show_delete_mode=False)
    model = SimpleNamespace(
        grid_model=grid_model,
        editing_side='default',
        current_category='player',
        default_data={'player': {'slots': []}},
        active_data={'player': {0: {'slots': []}}},
        selected_eid=0
    )
    panel_rect = pygame.Rect(0, 0, 100, 100)
    rects = view.draw(overlay, model, panel_rect)
    # expect keys
    expected = {'show_default', 'show_active', 'add_item', 'delete_item', 'save'}
    assert expected.issubset(rects.keys())
    # compatibility attributes updated
    assert view.show_default_rect == rects['show_default']
    assert view.show_active_rect == rects['show_active']
    assert view.add_item_rect == rects['add_item']
    assert view.delete_item_rect == rects['delete_item']
    assert view.save_rect == rects['save']
    # no errors during draw
    assert errors == []
