import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.tabs.tabs_event_handler import TabsEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # stub controller and view
    controller = SimpleNamespace(model=SimpleNamespace())
    # stub change_tab to record calls
    controller.change_tab = lambda tab: setattr(controller, 'called', tab)
    # define two rects
    default_rect = pygame.Rect(0, 0, 10, 10)
    ground_rect = pygame.Rect(20, 0, 10, 10)
    scroll_panel = SimpleNamespace(scroll_offset=5)
    view = SimpleNamespace(tab_rects=[default_rect, ground_rect], scroll_panel=scroll_panel)
    handler = TabsEventHandler(controller, view)
    return handler, controller, view


def test_no_click_event_returns_false(setup_handler):
    handler, controller, view = setup_handler
    event = SimpleNamespace(type=pygame.KEYDOWN)
    assert handler.handle(event) is False
    assert not hasattr(controller, 'called')


def test_click_outside_rects_returns_false(setup_handler):
    handler, controller, view = setup_handler
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(100, 100))
    assert handler.handle(event) is False
    assert not hasattr(controller, 'called')


def test_click_on_default_calls_change_tab(setup_handler):
    handler, controller, view = setup_handler
    # click inside default_rect
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(5, 5))
    result = handler.handle(event)
    assert result is True
    assert controller.called == 'default'
    assert view.scroll_panel.scroll_offset == 0


def test_click_on_ground_calls_change_tab(setup_handler):
    handler, controller, view = setup_handler
    # click inside ground_rect
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(25, 5))
    result = handler.handle(event)
    assert result is True
    assert controller.called == 'ground'
    assert view.scroll_panel.scroll_offset == 0
