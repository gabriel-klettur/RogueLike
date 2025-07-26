import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.size_panel.size_panel_events import SizePanelEventHandler
from roguelike_editors.tiles.size_panel.size_panel_state import SizePanelState

class DummyController:
    def __init__(self):
        self.drag_called = False
        self.drag_pos = None
        self.stop_called = False
        self.selected = None

    def drag(self, pos):
        self.drag_called = True
        self.drag_pos = pos

    def stop_drag(self):
        self.stop_called = True

    def on_size_selected(self, idx):
        self.selected = idx

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_start_drag_inside_panel_sets_dragging_and_offset():
    state = SizePanelState()
    controller = SimpleNamespace(editor_controller=SimpleNamespace(toolbar=SimpleNamespace(x=0, y=0, size=10, padding=5)))
    handler = SizePanelEventHandler(controller, state)
    x0, y0 = handler._initial_position()
    # click inside panel
    click_pos = (x0 + 5, y0 + 5)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': click_pos, 'button': 3})
    result = handler.handle_event(event)
    assert result is True
    assert state.dragging is True
    assert state.drag_offset == (5, 5)


def test_perform_drag_calls_controller_drag_and_returns_true():
    state = SizePanelState()
    state.dragging = True
    state.pos = (0, 0)
    state.drag_offset = (0, 0)
    controller = DummyController()
    handler = SizePanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (20, 30)})
    result = handler.handle_event(event)
    assert result is True
    assert controller.drag_called is True
    assert controller.drag_pos == (20, 30)


def test_stop_drag_calls_controller_and_returns_true():
    state = SizePanelState()
    state.dragging = True
    controller = DummyController()
    handler = SizePanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'pos': (0, 0), 'button': 3})
    result = handler.handle_event(event)
    assert result is True
    assert controller.stop_called is True


def test_select_size_calls_on_size_selected_and_returns_true():
    state = SizePanelState()
    state.visible = True
    # prepare option rects
    rect = pygame.Rect(0, 0, 10, 10)
    state.option_rects = {2: rect}
    controller = DummyController()
    handler = SizePanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (5, 5), 'button': 1})
    result = handler.handle_event(event)
    assert result is True
    assert controller.selected == 2


def test_unhandled_event_returns_false():
    state = SizePanelState()
    controller = DummyController()
    handler = SizePanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_a})
    assert handler.handle_event(event) is False
