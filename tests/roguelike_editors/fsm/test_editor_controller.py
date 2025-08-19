from __future__ import annotations

import importlib
from types import SimpleNamespace

import pytest

import roguelike_editors.fsm.fsm_editor_controller as mod


class _StubToolbarCtrl:
    def __init__(self):
        self.model = SimpleNamespace(active_tool='sets_list')
        self.render_calls = 0
        self.events = []

    def render(self, screen):
        self.render_calls += 1
        return (0, 0, 200, screen.get_size()[1])  # left column rect

    def handle_event(self, event):
        self.events.append(event)
        return getattr(event, 'type', None) == 'TOOL'


class _StubSetsPanelCtrl:
    def __init__(self):
        self.model = SimpleNamespace(visible=False, items=[], selected_index=0)
        self.renders = []
        self.events = []

    def render(self, screen, *, anchor):
        self.renders.append(anchor)
        # return its rect (anchor)
        return anchor

    def handle_event(self, event):
        self.events.append(event)
        if not self.model.visible:
            return False
        return getattr(event, 'type', None) == 'SETS'


class _StubGraphPanelCtrl:
    def __init__(self):
        self.model = SimpleNamespace(visible=False, selected_set_id=None, nodes=[], edges=[], zoom=1.0, pan_x=0.0, pan_y=0.0)
        self.renders = []
        self.events = []

    def render(self, screen, *, anchor):
        self.renders.append(anchor)
        return anchor

    def handle_event(self, event):
        self.events.append(event)
        return getattr(event, 'type', None) == 'GRAPH' and self.model.visible


class _Screen:
    def get_size(self):
        return (1600, 800)


@pytest.fixture(autouse=True)
def _isolate_services(monkeypatch: pytest.MonkeyPatch):
    # Stub service helpers used by controller.render
    monkeypatch.setattr(mod, 'compute_panel_anchor_next_to_toolbar', lambda toolbar_rect, screen_size, panel_size: (200, 0, *panel_size), raising=True)
    monkeypatch.setattr(mod, 'compute_graph_canvas_anchor', lambda sets_rect, screen_size, canvas_size: (520, 0, *canvas_size), raising=True)
    # Provide deterministic nodes/edges
    monkeypatch.setattr(mod, 'build_graph_from_set', lambda set_def, model, canvas: ([{"id": "Idle", "initial": True}], [{"id": "tr1", "from": "Idle", "to": "Idle"}]), raising=True)
    # Snapshot with a single set id
    monkeypatch.setattr(mod, 'get_snapshot', lambda: {"sets": [{"id": "TestSet", "initial": "Idle", "states": [{"id": "Idle"}], "transitions": []}]}, raising=True)


def test_toggle_visible_mirrors_config():
    cfg = importlib.import_module('roguelike_engine.config.config')
    cfg.DEBUG_ENTITIES = False

    c = mod.FsmEditorController()
    assert c.visible is False

    c.toggle_visible(True)
    assert c.visible is True
    assert cfg.DEBUG_ENTITIES is True

    c.toggle_visible(False)
    assert c.visible is False
    assert cfg.DEBUG_ENTITIES is False


def test_render_populates_sets_and_builds_graph_when_selected():
    c = mod.FsmEditorController()
    # Inject stubs
    c.toolbar_controller = _StubToolbarCtrl()
    c.sets_panel_controller = _StubSetsPanelCtrl()
    c.graph_panel_controller = _StubGraphPanelCtrl()

    # Make editor visible and sets panel effectively visible via active tool
    c.visible = True
    # Selected index is 0 by default

    # Render
    screen = _Screen()
    c.render(screen)

    # Sets panel got items populated from snapshot
    assert c.sets_panel_controller.model.items == ['TestSet']
    # Graph panel is visible and for selected set
    assert c.graph_panel_controller.model.visible is True
    assert c.graph_panel_controller.model.selected_set_id == 'TestSet'
    # Nodes/edges set from builder
    assert c.graph_panel_controller.model.nodes and c.graph_panel_controller.model.nodes[0]['id'] == 'Idle'
    assert c.graph_panel_controller.model.edges and c.graph_panel_controller.model.edges[0]['id'] == 'tr1'
    # Graph render called with computed anchor
    assert c.graph_panel_controller.renders[-1] == (520, 0, 800, 520)


def test_handle_event_routing_order_toolbar_then_sets_then_graph():
    c = mod.FsmEditorController()
    c.visible = True
    c.toolbar_controller = _StubToolbarCtrl()
    c.sets_panel_controller = _StubSetsPanelCtrl()
    c.graph_panel_controller = _StubGraphPanelCtrl()

    # Make sets panel visible to handle events
    c.sets_panel_controller.model.visible = True
    c.graph_panel_controller.model.visible = True

    # 1) Toolbar event short-circuits
    ev_toolbar = SimpleNamespace(type='TOOL')
    assert c.handle_event(ev_toolbar) is True
    # 2) Sets event handled if visible
    ev_sets = SimpleNamespace(type='SETS')
    assert c.handle_event(ev_sets) is True
    # 3) Unknown event should fall through
    ev_none = SimpleNamespace(type='OTHER')
    assert c.handle_event(ev_none) is False
