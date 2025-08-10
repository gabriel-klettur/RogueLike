import pytest
pytestmark = pytest.mark.skip("Right panel no longer has tabs; behavior moved to left panel")
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.tabs.tabs_event_handler import TabsEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    model = SimpleNamespace(editing_side=None)
    view = SimpleNamespace()
    # define default and active rects
    view.show_default_rect = pygame.Rect(0, 0, 10, 10)
    view.show_active_rect = pygame.Rect(20, 0, 10, 10)
    editor_controller = SimpleNamespace(model=model, view=view)
    controller = SimpleNamespace(editor_controller=editor_controller)
    handler = TabsEventHandler(controller)
    return handler, model, view

def test_handle_default_click(setup_handler):
    handler, model, view = setup_handler
    # click inside show_default_rect
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button':1, 'pos': (5,5)})
    result = handler.handle(event)
    assert result is True
    assert model.editing_side == 'default'

def test_handle_active_click(setup_handler):
    handler, model, view = setup_handler
    # click inside show_active_rect
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button':1, 'pos': (25,5)})
    result = handler.handle(event)
    assert result is True
    assert model.editing_side == 'active'

def test_handle_click_outside_returns_false(setup_handler):
    handler, model, view = setup_handler
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button':1, 'pos': (100,100)})
    result = handler.handle(event)
    assert result is False
    assert model.editing_side is None

def test_handle_non_mouse_event_returns_false(setup_handler):
    handler, model, view = setup_handler
    event = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_a})
    result = handler.handle(event)
    assert result is False
