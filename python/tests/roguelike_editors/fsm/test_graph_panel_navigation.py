from __future__ import annotations

from types import SimpleNamespace

import pygame
import pytest

from roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_events import FsmGraphPanelEventHandler
from roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_model import FsmGraphPanelModel
from roguelike_editors.fsm.fsm_graph_panel.model import to_world


class _Ctrl:
    def __init__(self):
        self.model = FsmGraphPanelModel(visible=True)
        self.view = SimpleNamespace(canvas_rect=pygame.Rect(50, 60, 400, 300))
        # Ensure toolbar path does not consume events
        self.toolbar_events = SimpleNamespace(handle_event=lambda event, **kw: False)
        self.toolbar = None


@pytest.fixture()
def ctrl():
    return _Ctrl()


def _local_from_pos(rect, pos):
    return (pos[0] - rect.left, pos[1] - rect.top)


def test_wheel_zoom_applies_and_persists(ctrl, monkeypatch):
    h = FsmGraphPanelEventHandler()
    rect = ctrl.view.canvas_rect

    # Spy persist_layout in events module (where it's imported)
    calls = {'n': 0}
    import roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_events as mod
    monkeypatch.setattr(mod, 'persist_layout', lambda m: calls.__setitem__('n', calls['n'] + 1), raising=True)

    pos = (rect.left + 120, rect.top + 80)
    lx, ly = _local_from_pos(rect, pos)
    wx0, wy0 = to_world(ctrl.model, lx, ly)

    ev = pygame.event.Event(pygame.MOUSEWHEEL, {'y': 1, 'pos': pos})
    consumed = h.handle_event(ctrl, ev)

    assert consumed is True
    assert ctrl.model.zoom > 1.0
    # World point at cursor remains invariant under zoom-at-point
    wx1, wy1 = to_world(ctrl.model, lx, ly)
    assert abs(wx1 - wx0) < 1e-6 and abs(wy1 - wy0) < 1e-6
    # Persist called once for wheel
    assert calls['n'] == 1


def test_button4_zoom_applies_and_persists(ctrl, monkeypatch):
    h = FsmGraphPanelEventHandler()
    rect = ctrl.view.canvas_rect

    calls = {'n': 0}
    import roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_events as mod
    monkeypatch.setattr(mod, 'persist_layout', lambda m: calls.__setitem__('n', calls['n'] + 1), raising=True)

    pos = (rect.left + 200, rect.top + 150)
    lx, ly = _local_from_pos(rect, pos)
    wx0, wy0 = to_world(ctrl.model, lx, ly)

    # Button 4 -> zoom in
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 4, 'pos': pos})
    assert h.handle_event(ctrl, ev) is True

    assert ctrl.model.zoom != 1.0
    wx1, wy1 = to_world(ctrl.model, lx, ly)
    assert abs(wx1 - wx0) < 1e-6 and abs(wy1 - wy0) < 1e-6
    assert calls['n'] == 1


def test_button5_zoom_out_applies_and_persists(ctrl, monkeypatch):
    h = FsmGraphPanelEventHandler()
    rect = ctrl.view.canvas_rect

    calls = {'n': 0}
    import roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_events as mod
    monkeypatch.setattr(mod, 'persist_layout', lambda m: calls.__setitem__('n', calls['n'] + 1), raising=True)

    pos = (rect.left + 240, rect.top + 120)
    lx, ly = _local_from_pos(rect, pos)
    wx0, wy0 = to_world(ctrl.model, lx, ly)

    z0 = ctrl.model.zoom
    # Button 5 -> zoom out
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 5, 'pos': pos})
    assert h.handle_event(ctrl, ev) is True

    assert ctrl.model.zoom < z0
    wx1, wy1 = to_world(ctrl.model, lx, ly)
    assert abs(wx1 - wx0) < 1e-6 and abs(wy1 - wy0) < 1e-6
    assert calls['n'] == 1


def test_middle_mouse_pan_flow_persists_on_end(ctrl, monkeypatch):
    h = FsmGraphPanelEventHandler()
    rect = ctrl.view.canvas_rect

    # Spy persist_layout in events module
    calls = {'n': 0}
    import roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_events as mod
    monkeypatch.setattr(mod, 'persist_layout', lambda m: calls.__setitem__('n', calls['n'] + 1), raising=True)

    # Start pan with MMB down inside canvas
    start_pos = (rect.left + 100, rect.top + 100)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 2, 'pos': start_pos})
    assert h.handle_event(ctrl, ev_down) is True
    assert ctrl.model.dragging_pan is True

    # While moving, the handler checks current mouse buttons; simulate mid-down
    monkeypatch.setattr(pygame.mouse, 'get_pressed', lambda n=3: (0, 1, 0), raising=False)

    move_pos = (start_pos[0] + 12, start_pos[1] + 7)
    ev_move = pygame.event.Event(pygame.MOUSEMOTION, {'pos': move_pos})
    assert h.handle_event(ctrl, ev_move) is True
    # Pan should reflect delta from start
    assert ctrl.model.pan_x != 0.0 or ctrl.model.pan_y != 0.0

    # End pan with MMB up; should persist
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, {'button': 2, 'pos': move_pos})
    assert h.handle_event(ctrl, ev_up) is True
    assert ctrl.model.dragging_pan is False
    assert calls['n'] == 1
