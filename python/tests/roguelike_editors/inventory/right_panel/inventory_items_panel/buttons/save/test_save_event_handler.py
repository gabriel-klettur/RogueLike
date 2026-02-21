import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_event_handler import SaveEventHandler

@pytest.fixture(autouse=True)

def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # Controller stub
    calls = {}
    def save_default():
        calls['default'] = True
    def save_active():
        calls['active'] = True
    ctrl = SimpleNamespace(save_default=save_default, save_active=save_active)
    # View and model stubs
    view = SimpleNamespace(save_rect=pygame.Rect(5, 5, 10, 10))
    model = SimpleNamespace(editing_side='default')
    editor_controller = SimpleNamespace(view=view, model=model)
    parent_controller = SimpleNamespace(editor_controller=editor_controller, save_default=ctrl.save_default, save_active=ctrl.save_active)
    handler = SaveEventHandler(parent_controller)
    return handler, calls, view, model


def test_handle_default_save(setup_handler):
    handler, calls, view, model = setup_handler
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)})
    assert handler.handle(event)
    assert calls.get('default', False)


def test_handle_active_save(setup_handler):
    handler, calls, view, model = setup_handler
    model.editing_side = 'active'
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (6, 6)})
    assert handler.handle(event)
    assert calls.get('active', False)


def test_handle_click_outside_returns_false(setup_handler):
    handler, calls, view, model = setup_handler
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 1, 'pos': (100, 100)})
    assert not handler.handle(event)
    assert calls == {}
