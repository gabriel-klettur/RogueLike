import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_event_handler import GridEventHandler

@pytest.fixture(autouse=True)

def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # Create minimal controller and model stub
    editor_controller = SimpleNamespace(view=SimpleNamespace(), model=SimpleNamespace())
    controller = SimpleNamespace(editor_controller=editor_controller, model=SimpleNamespace(), editor_controller_view=None)
    handler = GridEventHandler(controller)
    return handler


def test_handle_always_returns_false(setup_handler):
    handler = setup_handler
    # Test for mouse event
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (0, 0)})
    assert handler.handle(event) is False
    # Test for any other event
    event2 = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_a})
    assert handler.handle(event2) is False
