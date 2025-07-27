import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_view_panel.tiles_view_events import TilesViewPanelEventHandler
from roguelike_editors.tiles.tiles_view_panel.tiles_view_state import TilesViewPanelState

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def handler(monkeypatch):
    # Dummy panel with handle_event stub
    dummy_panel = SimpleNamespace(surface=pygame.Surface((10, 10)), pos=(5, 5))
    dummy_panel.handle_event = lambda ev, rect: True
    # Controller with dummy view.panel and stubs for drag/stop_drag
    controller = SimpleNamespace()
    controller.view = SimpleNamespace(panel=dummy_panel)
    controller.drag = lambda pos: setattr(controller, 'dragged', pos)
    controller.stop_drag = lambda: setattr(controller, 'stopped', True)
    # State with size for dragging
    state = TilesViewPanelState()
    state.size = (10, 10)
    return TilesViewPanelEventHandler(controller, state), controller, state, dummy_panel


def test_handle_event_true(handler):
    h, controller, state, panel = handler
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1)
    assert h.handle_event(ev)
    assert state.pos == panel.pos


def test_handle_event_false(handler):
    h, controller, state, panel = handler
    panel.handle_event = lambda ev, rect: False
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1)
    assert not h.handle_event(ev)
    assert state.pos is None


def test_event_detectors_and_drag_methods(handler):
    h, controller, state, panel = handler
    # _is_right_click_start
    ev_down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=3)
    assert h._is_right_click_start(ev_down)
    assert not h._is_right_click_start(SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1))
    # _is_drag_motion
    state.dragging = True
    ev_motion = SimpleNamespace(type=pygame.MOUSEMOTION)
    assert h._is_drag_motion(ev_motion)
    state.dragging = False
    assert not h._is_drag_motion(ev_motion)
    # _is_right_click_end
    state.dragging = True
    ev_up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=3)
    assert h._is_right_click_end(ev_up)
    assert not h._is_right_click_end(SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1))
    # _start_drag inside bounds
    state.size = (10, 10)
    state.pos = None
    result = h._start_drag((5, 5))
    assert result
    assert state.dragging
    assert state.drag_offset == (5, 5)
    # click outside bounds
    state.dragging = False
    state.drag_offset = (0, 0)
    assert not h._start_drag((20, 20))
    # _perform_drag
    called = {}
    controller.drag = lambda pos: called.setdefault('pos', pos)
    assert h._perform_drag((2, 3))
    assert called['pos'] == (2, 3)
    # _stop_drag
    called2 = {}
    controller.stop_drag = lambda: called2.setdefault('called', True)
    assert h._stop_drag()
    assert called2['called']
    # _get_initial_position override
    state.pos = (8, 8)
    assert h._get_initial_position() == (8, 8)
    # _get_initial_position fallback (no pos, no size)
    state.pos = None
    state.size = None
    assert h._get_initial_position() == (0, 0)
