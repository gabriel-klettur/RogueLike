import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_event_handler import AddItemEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # Dummy controller with start, select, confirm spies
    controller = SimpleNamespace()
    controller.model = SimpleNamespace(show_item_list=False, show_quantity_input=False)
    controller.start_add_item = lambda: setattr(controller.model, 'show_item_list', True)
    controller.select_item = lambda item: setattr(controller.model, 'selected_item', item)
    controller.confirm_quantity = lambda q: setattr(controller.model, 'confirmed', q)
    # Dummy view with add_item_rect and header
    view = SimpleNamespace(add_item_rect=pygame.Rect(0, 0, 10, 10),
                           item_list_header_rect=pygame.Rect(0, 0, 5, 5),
                           item_list_dragging=False,
                           item_list_drag_offset=pygame.Vector2(0, 0),
                           item_list_scroll_panel=None,
                           item_list_panel_rect=None,
                           add_to_inventory_button_rect=None,
                           font=None)
    controller.editor_controller = SimpleNamespace(view=view)
    handler = AddItemEventHandler(controller)
    return handler, controller, view


def test_click_add_item_triggers_start(setup_handler):
    handler, controller, view = setup_handler
    controller.model.show_item_list = False
    # click inside rect
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (5, 5)})
    result = handler.handle(event)
    assert result
    assert controller.model.show_item_list


def test_drag_start_and_motion_and_end(setup_handler):
    handler, controller, view = setup_handler
    # simulate show_item_list True
    controller.model.show_item_list = True
    # drag start
    event_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (1, 1)})
    result_down = handler.handle(event_down)
    assert result_down
    assert view.item_list_dragging
    # motion
    event_move = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (3, 4)})
    view.item_list_dragging = True
    result_move = handler.handle(event_move)
    assert result_move
    assert view.item_list_drag_offset == pygame.Vector2(3, 4) - view.item_list_drag_start_pos
    # drag end
    view.item_list_dragging = True
    event_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (3, 4)})
    result_up = handler.handle(event_up)
    assert result_up
    assert not view.item_list_dragging


def test_quantity_input_handling(setup_handler):
    handler, controller, view = setup_handler
    # setup quantity input mode
    controller.model.show_quantity_input = True
    controller.model.quantity = 1
    # key digit
    event_digit = pygame.event.Event(pygame.KEYDOWN, {'unicode': '3', 'key': pygame.K_3})
    result_digit = handler.handle(event_digit)
    assert result_digit and controller.model.quantity == 13
    # backspace
    event_back = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_BACKSPACE})
    prev = controller.model.quantity
    result_back = handler.handle(event_back)
    assert result_back and controller.model.quantity == 1
    # escape
    controller.model.show_item_list = True
    event_esc = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_ESCAPE})
    result_esc = handler.handle(event_esc)
    assert result_esc and not controller.model.show_quantity_input and not controller.model.show_item_list
    # return key
    controller.model.show_quantity_input = True
    controller.model.quantity = 5
    event_return = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_RETURN})
    result_ret = handler.handle(event_return)
    assert result_ret and controller.model.confirmed == 5
