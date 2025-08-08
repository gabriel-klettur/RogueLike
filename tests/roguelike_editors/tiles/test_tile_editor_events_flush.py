import pygame
import types
import pytest

from roguelike_editors.tiles.tile_editor_events import TileEditorEventHandler
from roguelike_editors.tiles.tile_editor_state import TileEditorState

class FakePickerState:
    def __init__(self):
        self.open = False
        self.pos = (0, 0)
        self.surface = pygame.Surface((200, 150))

class FakePicker:
    def __init__(self, over=False):
        self._over = over
        self.picker_state = FakePickerState()
    def is_over(self, pos):
        return self._over

class FakeController:
    def __init__(self):
        self.picker = FakePicker(over=False)
        self.toolbar = types.SimpleNamespace()
        self.view_panel_controller = object()
        self.title_controller = object()
        self.collision_panel_controller = object()
        self.layers_panel_controller = object()
        self.size_panel_controller = object()
        self.flushes = 0
    def start_brush(self):
        pass
    def flush_brush(self, map, camera):
        self.flushes += 1
    def select_tile_at(self, *a, **k):
        pass
    def apply_brush(self, *a, **k):
        pass
    def apply_eyedropper(self, *a, **k):
        pass

class DummyState:
    running = True

@pytest.fixture
def handler(monkeypatch):
    editor_state = TileEditorState()
    ctrl = FakeController()
    h = TileEditorEventHandler(DummyState(), editor_state, ctrl)
    # Override panel tools to no-op to avoid external dependencies
    h.toolbar_tool = types.SimpleNamespace(handle_event=lambda ev: None, handle_click=lambda ev, m: False)
    h.view_panel_tool = types.SimpleNamespace(handle_event=lambda ev, c, m: None)
    h.layers_panel_tool = types.SimpleNamespace(handle_event=lambda ev, c, m: None)
    h.collision_panel_tool = types.SimpleNamespace(handle_event=lambda ev: False)
    h.size_panel_tool = types.SimpleNamespace(handle_event=lambda ev: False)
    h.title_tool = types.SimpleNamespace(handle_event=lambda ev: None)
    # Stub picker tool so it consumes clicks and doesn't touch real controller
    h.picker_tool = types.SimpleNamespace(handle_event=lambda *a, **k: None, handle_click=lambda *a, **k: True)
    return h, ctrl

class DummyCam: pass
class DummyMap: pass


def _mouse_event(t, pos=(0,0), button=1, rel=(0,0), y=0):
    e = types.SimpleNamespace(type=t, pos=pos, button=button, rel=rel, y=y)
    return e


def test_no_flush_when_not_dragging(handler):
    h, ctrl = handler
    h.editor_state.current_tool = "brush"
    h.editor_state.brush_dragging = False
    events = [
        _mouse_event(pygame.MOUSEBUTTONUP, pos=(10,10), button=1),
    ]
    h.handle(events, DummyCam(), DummyMap())
    assert ctrl.flushes == 0


def test_flush_when_dragging(handler):
    h, ctrl = handler
    h.editor_state.current_tool = "brush"
    h.editor_state.brush_dragging = True
    events = [
        _mouse_event(pygame.MOUSEBUTTONUP, pos=(10,10), button=1),
    ]
    h.handle(events, DummyCam(), DummyMap())
    assert ctrl.flushes == 1
    assert h.editor_state.brush_dragging is False


def test_click_inside_picker_does_not_set_drag_or_flush(handler):
    h, ctrl = handler
    h.editor_state.current_tool = "brush"
    h.editor_state.picker_state.open = True
    # Make picker consume the click
    ctrl.picker._over = True

    # Mouse down will call controller.start_brush, but _on_mouse_down will return early and not set brush_dragging
    down = _mouse_event(pygame.MOUSEBUTTONDOWN, pos=(150,80), button=1)
    up = _mouse_event(pygame.MOUSEBUTTONUP, pos=(150,80), button=1)

    h.handle([down, up], DummyCam(), DummyMap())
    assert ctrl.flushes == 0
    assert h.editor_state.brush_dragging is False
