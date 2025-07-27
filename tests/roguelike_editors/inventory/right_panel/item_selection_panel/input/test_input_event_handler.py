import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.input.input_event_handler import InputEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # stub controller with model
    model = SimpleNamespace(quantity=4)
    controller = SimpleNamespace(model=model)
    # stub set_quantity to record calls
    def set_qty(x):
        controller.qty_set = x
    controller.set_quantity = set_qty
    # stub view and its text_input
    text_input = SimpleNamespace(active=False, text='')
    text_input.handle_event = lambda event: False
    text_input.activate = lambda initial_text, select_all: setattr(text_input, 'activated', (initial_text, select_all))
    view = SimpleNamespace(input_rect=pygame.Rect(1, 2, 3, 4), text_input=text_input)
    handler = InputEventHandler(controller, view)
    return handler, controller, view, text_input


def test_handle_click_inside_activates_text_input(setup_handler):
    handler, controller, view, text_input = setup_handler
    event = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(2, 3))
    result = handler.handle(event)
    assert result is True
    assert hasattr(text_input, 'activated')
    initial_text, select_all = text_input.activated
    assert initial_text == '4'
    assert select_all is True


def test_handle_text_input_event_triggers_set_quantity(setup_handler):
    handler, controller, view, text_input = setup_handler
    # stub handle_event to return True and set text
    text_input.handle_event = lambda event: True
    text_input.text = '7'
    event = SimpleNamespace(type=pygame.KEYDOWN)
    result = handler.handle(event)
    assert result is True
    assert controller.qty_set == '7'


def test_handle_non_relevant_event_returns_false(setup_handler):
    handler, controller, view, text_input = setup_handler
    event = SimpleNamespace(type=pygame.KEYDOWN)
    # handle_event returns False by default and click outside not tested
    # simulate click outside
    event2 = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(100, 100))
    assert handler.handle(event) is False
    assert handler.handle(event2) is False
