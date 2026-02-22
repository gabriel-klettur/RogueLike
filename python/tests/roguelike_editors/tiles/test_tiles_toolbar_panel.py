import os
import pygame
import pytest

# Headless pygame for CI
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
pygame.init()

from roguelike_editors.tiles.tile_editor_state import TileEditorState
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_events import TileToolbarEventHandler
from roguelike_editors.tiles.tiles_editor_config import TOOLS


class FakeSizePanelState:
    def __init__(self):
        self.visible = False


class FakeSizePanelController:
    def __init__(self):
        self.state = FakeSizePanelState()

    def show(self):
        self.state.visible = True

    def toggle(self):
        self.state.visible = not self.state.visible


class FakeEditorController:
    def __init__(self):
        self.size_panel_controller = FakeSizePanelController()
        self.start_brush_calls = 0
        self.flush_brush_calls = 0

    def start_brush(self):
        self.start_brush_calls += 1

    def flush_brush(self, *_args, **_kwargs):
        self.flush_brush_calls += 1


class FakeToolbarController:
    def __init__(self):
        self.editor_state = TileEditorState()
        self.editor_controller = FakeEditorController()
        # Layout used by drag logic
        self.x, self.y = 10, 70
        self.size, self.padding = 64, 8
        # Provide icon rects so clicks are consumed
        self.icon_rects = {}
        cx, cy = 200, 200
        for i, tool in enumerate(TOOLS):
            # Stack rects vertically to simulate the view
            r = pygame.Rect(cx, cy + i * (self.size + self.padding), self.size, self.size)
            self.icon_rects[tool] = r
        # Track calls from delete/default handlers
        self.deleted_calls = 0
        self.default_calls = 0
        # Drag state passthrough helpers
        self._drag_calls = 0
        self._stop_drag_calls = 0
        self.size = 64
        self.padding = 8

    # Methods invoked by handlers
    def delete_tile(self, *_args, **_kwargs):
        self.deleted_calls += 1

    def set_default(self, *_args, **_kwargs):
        self.default_calls += 1

    def drag(self, mouse_pos):
        ts = self.editor_state.toolbar_state
        ts.pos = (mouse_pos[0] - ts.drag_offset[0], mouse_pos[1] - ts.drag_offset[1])
        ts.dragging = True
        self._drag_calls += 1

    def stop_drag(self):
        ts = self.editor_state.toolbar_state
        ts.dragging = False
        self._stop_drag_calls += 1


@pytest.fixture()
def toolbar_handler():
    ctrl = FakeToolbarController()
    handler = TileToolbarEventHandler(ctrl)
    return ctrl, handler


def _click_event_at(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos, "button": 1})


def _right_down(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": pos, "button": 3})


def _right_up(pos):
    return pygame.event.Event(pygame.MOUSEBUTTONUP, {"pos": pos, "button": 3})


def _motion(pos):
    return pygame.event.Event(pygame.MOUSEMOTION, {"pos": pos})


# --- Tool clicks ---

def test_select_sets_tool_and_closes_picker(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["select"]
    ev = _click_event_at(rect.center)
    consumed = handler.handle_click(ev, map=None, camera=None)
    assert consumed is True
    assert ctrl.editor_state.current_tool == "select"
    # handler closes picker only when tool_name == "select"
    assert ctrl.editor_state.picker_state.open is False


def test_eyedropper_sets_tool_and_keeps_view_open(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["eyedropper"]
    ev = _click_event_at(rect.center)
    consumed = handler.handle_click(ev, map=None, camera=None)
    assert consumed is True
    assert ctrl.editor_state.current_tool == "eyedropper"
    assert ctrl.editor_state.toolbar_state.view_active is True


def test_brush_first_click_shows_size_panel_and_picker(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["brush"]
    ev = _click_event_at(rect.center)
    handler.handle_click(ev, map=None, camera=None)
    assert ctrl.editor_state.current_tool == "brush"
    assert ctrl.editor_controller.size_panel_controller.state.visible is True
    assert ctrl.editor_state.picker_state.open is True
    assert ctrl.editor_state.toolbar_state.view_active is True


def test_brush_second_click_toggles_off_and_returns_select(toolbar_handler):
    ctrl, handler = toolbar_handler
    # First enable brush and make panel visible
    handler._handle_brush("brush", map=None, camera=None)
    assert ctrl.editor_controller.size_panel_controller.state.visible is True
    # Second click should toggle panel -> invisible and current_tool -> select
    rect = ctrl.icon_rects["brush"]
    ev = _click_event_at(rect.center)
    handler.handle_click(ev, map=None, camera=None)
    assert ctrl.editor_controller.size_panel_controller.state.visible is False
    assert ctrl.editor_state.picker_state.open is False
    assert ctrl.editor_state.current_tool == "select"


def test_view_toggles_view_active(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["view"]
    ev = _click_event_at(rect.center)
    prev = ctrl.editor_state.toolbar_state.view_active
    handler.handle_click(ev, map=None, camera=None)
    assert ctrl.editor_state.toolbar_state.view_active == (not prev)


def test_view_layers_toggles_layers_view_open(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["view_layers"]
    ev = _click_event_at(rect.center)
    prev = ctrl.editor_state.toolbar_state.layers_view_open
    handler.handle_click(ev, map=None, camera=None)
    assert ctrl.editor_state.toolbar_state.layers_view_open == (not prev)


def test_view_collisions_cycles_modes_and_picker(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["view_collisions"]
    ev = _click_event_at(rect.center)
    ts = ctrl.editor_state.toolbar_state

    # 1st click: off -> only
    handler.handle_click(ev, map=None, camera=None)
    assert ts.show_collisions is True and ts.show_collisions_overlay is False
    assert ts.collision_picker_open is True
    assert ctrl.editor_state.current_tool == "brush"
    assert ctrl.editor_state.picker_state.open is False

    # 2nd click: only -> overlay
    handler.handle_click(ev, map=None, camera=None)
    assert ts.show_collisions is True and ts.show_collisions_overlay is True

    # 3rd click: overlay -> off
    handler.handle_click(ev, map=None, camera=None)
    assert ts.show_collisions is False and ts.show_collisions_overlay is False
    assert ts.collision_picker_open is False
    assert ts.collision_choice is None


def test_delete_toggles_and_calls_delete_and_batch(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["delete"]
    ev = _click_event_at(rect.center)

    # First click enters delete mode and applies immediate delete within a batched op
    handler.handle_click(ev, map=object(), camera=object())
    assert ctrl.editor_state.current_tool == "delete"
    assert ctrl.editor_controller.start_brush_calls == 1
    assert ctrl.deleted_calls == 1
    assert ctrl.editor_controller.flush_brush_calls == 1

    # Second click returns to select
    handler.handle_click(ev, map=object(), camera=object())
    assert ctrl.editor_state.current_tool == "select"


def test_default_toggles_and_applies_immediately_when_tile_selected(toolbar_handler):
    ctrl, handler = toolbar_handler
    rect = ctrl.icon_rects["default"]
    ev = _click_event_at(rect.center)

    # With no selected tile: just toggle tool
    ctrl.editor_state.selected_tile = None
    handler.handle_click(ev, map=object(), camera=object())
    assert ctrl.editor_state.current_tool == "default"
    assert ctrl.default_calls == 0

    # With a selected tile: immediate apply within batched op
    ctrl.editor_state.selected_tile = object()
    handler.handle_click(ev, map=object(), camera=object())
    assert ctrl.editor_state.current_tool == "default"
    assert ctrl.editor_controller.start_brush_calls >= 1
    assert ctrl.default_calls >= 1
    assert ctrl.editor_controller.flush_brush_calls >= 1

    # Press again returns to select
    handler.handle_click(ev, map=object(), camera=object())
    assert ctrl.editor_state.current_tool == "select"


# --- Drag with right mouse button ---

def test_toolbar_drag_start_move_stop(toolbar_handler):
    ctrl, handler = toolbar_handler
    ts = ctrl.editor_state.toolbar_state

    # Click right inside the toolbar panel rect so start_drag triggers
    # Compute a point guaranteed to be inside the panel
    panel_top_left = (ctrl.x + 5, ctrl.y + 5)
    ev_down = _right_down(panel_top_left)
    consumed = handler.handle_event(ev_down)
    assert consumed is True
    assert ts.dragging is True

    # Move mouse and ensure controller.drag was invoked and pos updated
    move_pos = (panel_top_left[0] + 20, panel_top_left[1] + 10)
    ev_motion = _motion(move_pos)
    consumed = handler.handle_event(ev_motion)
    assert consumed is True
    assert ts.dragging is True
    assert ts.pos is not None

    # Release right button to stop dragging
    ev_up = _right_up(move_pos)
    consumed = handler.handle_event(ev_up)
    assert consumed is True
    assert ts.dragging is False
