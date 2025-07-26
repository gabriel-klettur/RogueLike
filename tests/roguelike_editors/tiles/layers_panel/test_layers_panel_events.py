import pygame
import pytest
from types import SimpleNamespace
from roguelike_editors.tiles.layers_panel.layers_panel_events import LayersPanelEventHandler
from roguelike_editors.tiles.layers_panel.layers_panel_states import LayersPanelState


class DummyController:
    def __init__(self, state=None):
        self.state = state
        # toolbar_state for toggle tests
        self.editor_state = SimpleNamespace(
            toolbar_state=SimpleNamespace(visible_layers={}, show_buildings=False)
        )
        self.drag_called = False
        self.drag_pos = None
        self.stop_called = False

    def drag(self, pos):
        self.drag_called = True
        self.drag_pos = pos

    def stop_drag(self):
        self.stop_called = True
        if self.state is not None:
            self.state.dragging = False


@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_handle_event_right_start_drag_inside():
    state = LayersPanelState()
    state.pos = (50, 60)
    controller = DummyController()
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (55, 65), 'button': 3})
    result = handler.handle_event(event)
    assert result is True
    assert state.dragging is True
    assert state.drag_offset == (5, 5)


def test_handle_event_right_start_drag_outside():
    state = LayersPanelState()
    state.pos = (50, 60)
    controller = DummyController()
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (10, 10), 'button': 3})
    result = handler.handle_event(event)
    assert result is False
    assert state.dragging is False


def test_handle_event_motion_calls_drag_and_returns_true():
    state = LayersPanelState()
    state.dragging = True
    controller = DummyController()
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (100, 200)})
    result = handler.handle_event(event)
    assert result is True
    assert controller.drag_called is True
    assert controller.drag_pos == (100, 200)


def test_handle_event_stop_drag_calls_stop_and_returns_true():
    state = LayersPanelState()
    state.dragging = True
    controller = DummyController(state)
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {'pos': (0, 0), 'button': 3})
    result = handler.handle_event(event)
    assert result is True
    assert controller.stop_called is True


def test_handle_event_left_toggle_generic_layer():
    from pygame import Rect

    state = LayersPanelState()
    state.visible_layers = {'foo': False}
    state.option_rects = {'foo': pygame.Rect(0, 0, 10, 10)}
    controller = DummyController()
    controller.editor_state.toolbar_state.visible_layers = {'foo': False}
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (5, 5), 'button': 1})
    result = handler.handle_event(event)
    assert result is True
    assert state.visible_layers['foo'] is True
    assert controller.editor_state.toolbar_state.visible_layers['foo'] is True


def test_handle_event_left_toggle_buildings():
    state = LayersPanelState()
    state.option_rects = {'buildings': pygame.Rect(0, 0, 20, 20)}
    controller = DummyController()
    controller.editor_state.toolbar_state.show_buildings = False
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (10, 10), 'button': 1})
    result = handler.handle_event(event)
    assert result is True
    assert controller.editor_state.toolbar_state.show_buildings is True


def test_handle_event_unhandled_returns_false():
    state = LayersPanelState()
    controller = DummyController()
    handler = LayersPanelEventHandler(controller, state)
    event = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_a})
    assert handler.handle_event(event) is False
