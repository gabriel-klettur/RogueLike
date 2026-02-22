import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_event_handler import DeleteEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # Modelo y controlador stub
    delete_model = SimpleNamespace(show_delete_mode=False, show_delete_quantity_input=False, delete_quantity=3)
    delete_ctrl = SimpleNamespace(delete_item=lambda idx, qty: setattr(delete_model, 'deleted', (idx, qty)))
    delete_ctrl.editor_controller = SimpleNamespace(model=None)
    grid_view = SimpleNamespace(
        delete_qty_input=SimpleNamespace(last_rect=pygame.Rect(0, 0, 1, 1), text='3', handle_event=lambda ev: False),
        delete_qty_input_rect=pygame.Rect(0, 0, 1, 1)
    )
    grid_view.tabs_view = SimpleNamespace(get_slots_data=lambda model: [])
    editor_view = SimpleNamespace(
        delete_item_rect=pygame.Rect(5, 5, 10, 10),
        grid_view=grid_view,
        margin=2,
        slot_size=10,
        left_panel_rect=pygame.Rect(0, 0, 50, 50)
    )
    editor_controller = SimpleNamespace(view=editor_view)
    parent_controller = SimpleNamespace(delete_controller=delete_ctrl, model=SimpleNamespace(delete=delete_model), editor_controller=editor_controller)
    handler = DeleteEventHandler(parent_controller)
    return handler, delete_model


def test_toggle_delete_mode(setup_handler):
    handler, model = setup_handler
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)})
    assert handler.handle(event)
    assert model.show_delete_mode
    assert model.show_delete_quantity_input
    assert model.delete_quantity == 1


def test_exit_delete_mode_on_second_click(setup_handler):
    handler, model = setup_handler
    # Activate
    handler.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)}))
    # Deactivate
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)})
    assert handler.handle(event)
    assert not model.show_delete_mode
    assert not model.show_delete_quantity_input


def test_click_outside_when_active_exits_mode(setup_handler):
    handler, model = setup_handler
    handler.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)}))
    assert model.show_delete_mode
    # Click outside items
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (100, 100)})
    assert handler.handle(event)
    assert not model.show_delete_mode
    assert not model.show_delete_quantity_input
