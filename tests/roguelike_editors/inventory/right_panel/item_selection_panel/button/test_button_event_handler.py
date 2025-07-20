import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.button.button_event_handler import ButtonEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # create fake controller and grid_controller
    controller = SimpleNamespace(model=SimpleNamespace(), set_quantity=lambda x: setattr(controller, 'qty_set', x), confirm=lambda: ('itm', 9))
    # we need reference to controller inside lambda; workaround bind later
    handler = None
    grid_controller = SimpleNamespace(select_item=lambda item: setattr(grid_controller, 'selected', item), confirm_quantity=lambda q: setattr(grid_controller, 'confirmed', q))
    # dummy view
    rect = pygame.Rect(1, 2, 10, 10)
    text_input = SimpleNamespace(text='5', active=True)
    view = SimpleNamespace(add_button_rect=rect, text_input=text_input)
    # fix controller.set_quantity closure
    controller = SimpleNamespace(model=controller.model, set_quantity=lambda x: setattr(controller, 'qty_set', x), confirm=lambda: ('itm', 9))
    handler = ButtonEventHandler(grid_controller, controller, view)
    return handler, controller, grid_controller, view


def test_handle_click_inside_triggers_actions(setup_handler):
    handler, controller, grid_ctrl, view = setup_handler
    # stub confirm to return ('itemA', 3)
    controller.confirm = lambda: ('itemA', 3)
    # simulate click inside rect
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(5, 6))
    result = handler.handle(event)
    assert result is True
    assert controller.qty_set == view.text_input.text
    assert grid_ctrl.selected == 'itemA'
    assert grid_ctrl.confirmed == 3
    assert view.text_input.active is False


def test_handle_click_outside_returns_false(setup_handler):
    handler, _, _, view = setup_handler
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(100, 100))
    result = handler.handle(event)
    assert result is False


def test_handle_non_click_event_returns_false(setup_handler):
    handler, _, _, _ = setup_handler
    event = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_RETURN)
    assert handler.handle(event) is False
