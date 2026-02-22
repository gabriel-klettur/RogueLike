import pytest
import pygame
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_events import TilesCollisionPanelEventHandler

# Dummy classes for testing
class DummyToolbarState:
    def __init__(self):
        self.collision_choice = None
        self.collision_picker_pos = (0, 0)
        self.collision_picker_panel_size = (0, 0)
        self.collision_picker_dragging = False
        self.collision_picker_drag_offset = (0, 0)

class DummyEditorState:
    def __init__(self):
        self.toolbar_state = DummyToolbarState()

class DummyController:
    def __init__(self):
        self.editor_state = DummyEditorState()

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def handler():
    controller = DummyController()
    panel_state = type('PanelState', (), {})()
    panel_state.option_rects = {}
    return TilesCollisionPanelEventHandler(controller, panel_state)

def test_select_collision_sets_choice_and_returns_true(handler):
    # Prepare option rect and simulate click inside it
    handler.state.option_rects = {'X': pygame.Rect(10, 10, 5, 5)}
    pos = (12, 12)
    result = handler._select_collision(pos)
    assert result is True
    assert handler.controller.editor_state.toolbar_state.collision_choice == 'X'

def test_select_collision_returns_false_outside(handler):
    handler.state.option_rects = {'X': pygame.Rect(0, 0, 5, 5)}
    pos = (10, 10)
    result = handler._select_collision(pos)
    assert result is False

def test_handle_event_left_click_on_option(handler):
    handler.state.option_rects = {'A': pygame.Rect(0, 0, 10, 10)}
    evt = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))
    assert handler.handle_event(evt) is True
    assert handler.controller.editor_state.toolbar_state.collision_choice == 'A'

def test_handle_event_left_click_inside_panel(handler):
    # No option selected but inside panel bounds
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_pos = (0, 0)
    toolbar.collision_picker_panel_size = (20, 20)
    handler.state.option_rects = {}
    evt = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(10, 10))
    assert handler.handle_event(evt) is True

def test_handle_event_left_click_outside_panel(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_pos = (0, 0)
    toolbar.collision_picker_panel_size = (5, 5)
    handler.state.option_rects = {}
    evt = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=(10, 10))
    assert handler.handle_event(evt) is False

def test_handle_event_right_click_start_drag(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_pos = (0, 0)
    toolbar.collision_picker_panel_size = (20, 20)
    evt = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(5, 5))
    assert handler.handle_event(evt) is True
    assert toolbar.collision_picker_dragging is True
    assert toolbar.collision_picker_drag_offset == (5, 5)

def test_handle_event_right_click_outside_drag(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_pos = (100, 100)
    toolbar.collision_picker_panel_size = (10, 10)
    evt = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(0, 0))
    assert handler.handle_event(evt) is False
    assert handler.controller.editor_state.toolbar_state.collision_picker_dragging is False

def test_handle_event_mouse_motion_dragging(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_dragging = True
    toolbar.collision_picker_drag_offset = (2, 3)
    evt = pygame.event.Event(pygame.MOUSEMOTION, pos=(10, 10))
    assert handler.handle_event(evt) is True
    assert toolbar.collision_picker_pos == (8, 7)

def test_handle_event_mouse_motion_not_dragging(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_dragging = False
    evt = pygame.event.Event(pygame.MOUSEMOTION, pos=(10, 10))
    assert handler.handle_event(evt) is False

def test_handle_event_right_button_up(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_dragging = True
    evt = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3)
    assert handler.handle_event(evt) is True
    assert toolbar.collision_picker_dragging is False

def test_handle_event_right_button_up_not_dragging(handler):
    toolbar = handler.controller.editor_state.toolbar_state
    toolbar.collision_picker_dragging = False
    evt = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3)
    assert handler.handle_event(evt) is False
