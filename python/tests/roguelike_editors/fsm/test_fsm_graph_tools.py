import types
import pygame

from roguelike_editors.fsm.fsm_graph_panel.fsm_graph_panel_model import FsmGraphPanelModel
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.add_node.add_node_events import AddNodeEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.connect.connect_events import ConnectEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.disconnect.disconnect_events import DisconnectEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.clone.clone_events import CloneEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.delete_node.delete_node_events import DeleteNodeEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.mark_ini.mark_ini_events import MarkIniEventHandler
from roguelike_editors.fsm.fsm_graph_panel.toolbar_graph_panel.mark_end.mark_end_events import MarkEndEventHandler


class _FakeView:
    def __init__(self, rect):
        self.canvas_rect = rect
        self.edge_paths = {}


def _fake_controller():
    c = types.SimpleNamespace()
    c._persist_sets_structural = lambda: None
    c._persist_layout = lambda: None
    return c


def _mk_click(x, y):
    return pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"pos": (x, y), "button": 1})


def test_add_node_adds_one_at_click_position():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = AddNodeEventHandler()

    evt = _mk_click(200, 100)
    consumed = h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    assert consumed is True
    assert len(model.nodes) == 1
    n = model.nodes[0]
    assert n["x"] <= 200 <= n["x"] + n.get("w", 120)
    assert n["y"] <= 100 <= n["y"] + n.get("h", 60)


def test_connect_creates_edge_between_two_clicked_nodes():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = ConnectEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40},
        {"id": "B", "label": "B", "x": 200, "y": 50, "w": 80, "h": 40},
    ]

    evt1 = _mk_click(60, 60)
    evt2 = _mk_click(210, 60)
    assert h.handle_event(ctl, evt1, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.connect_source_node_id == "A"
    assert h.handle_event(ctl, evt2, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.connect_source_node_id is None
    assert { (e.get("from"), e.get("to")) for e in model.edges } == { ("A", "B") }


def test_disconnect_removes_edge_between_two_clicked_nodes():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = DisconnectEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40},
        {"id": "B", "label": "B", "x": 200, "y": 50, "w": 80, "h": 40},
    ]
    model.edges = [{"from": "A", "to": "B"}]

    evt1 = _mk_click(60, 60)
    evt2 = _mk_click(210, 60)
    assert h.handle_event(ctl, evt1, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.connect_source_node_id == "A"
    assert h.handle_event(ctl, evt2, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.connect_source_node_id is None
    assert model.edges == []


def test_clone_creates_offset_duplicate():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = CloneEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40},
    ]

    evt = _mk_click(60, 60)
    assert h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    assert len(model.nodes) == 2
    a = model.nodes[0]
    b = model.nodes[1]
    assert b["x"] == a["x"] + 20 and b["y"] == a["y"] + 20


def test_delete_removes_node_and_incident_edges():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = DeleteNodeEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40},
        {"id": "B", "label": "B", "x": 200, "y": 50, "w": 80, "h": 40},
    ]
    model.edges = [
        {"id": "e1", "from": "A", "to": "B"},
        {"id": "e2", "from": "B", "to": "A"},
    ]

    evt = _mk_click(60, 60)  # inside node A
    assert h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    ids = [n.get("id") for n in model.nodes]
    assert "A" not in ids
    for e in model.edges:
        assert e.get("from") != "A" and e.get("to") != "A"


def test_mark_ini_sets_unique_initial():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = MarkIniEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40, "initial": False},
        {"id": "B", "label": "B", "x": 200, "y": 50, "w": 80, "h": 40, "initial": False},
    ]

    evt = _mk_click(210, 60)  # on B
    assert h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    flags = {n.get("id"): bool(n.get("initial")) for n in model.nodes}
    assert flags == {"A": False, "B": True}


def test_mark_end_toggles_terminal():
    model = FsmGraphPanelModel(visible=True)
    view = _FakeView(pygame.Rect(0, 0, 800, 600))
    ctl = _fake_controller()
    h = MarkEndEventHandler()

    model.nodes = [
        {"id": "A", "label": "A", "x": 50, "y": 50, "w": 80, "h": 40, "terminal": False},
    ]

    evt = _mk_click(60, 60)
    assert h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.nodes[0].get("terminal") is True
    # Toggle off
    assert h.handle_event(ctl, evt, model=model, view=view, canvas_rect=view.canvas_rect)
    assert model.nodes[0].get("terminal") is False
