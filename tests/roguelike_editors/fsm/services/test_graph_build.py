import types

import pytest

from roguelike_editors.fsm.services import graph_build as gb


class _StubModel:
    def __init__(self) -> None:
        self.zoom = 1.0
        self.pan_x = 0.0
        self.pan_y = 0.0
        self.legend_collapsed = False


def test_build_graph_empty_returns_empty_lists():
    nodes, edges = gb.build_graph_from_set(None, _StubModel())
    assert nodes == []
    assert edges == []


def test_build_graph_basic_nodes_edges_and_initial():
    set_def = {
        "id": "set1",
        "states": [
            {"id": "A"},
            {"id": "B"},
        ],
        "transitions": [
            {"from": "A", "to": "B", "when": "go"},
            {"from": "B", "to": "A"},
        ],
        "initial": "A",
    }
    model = _StubModel()

    nodes, edges = gb.build_graph_from_set(set_def, model, canvas=(400, 300))

    assert {n["id"] for n in nodes} == {"A", "B"}
    assert any(n["initial"] for n in nodes if n["id"] == "A")
    assert len(edges) == 2
    # edge label comes from 'when'
    assert any(e.get("label") == "go" for e in edges)


def test_build_graph_applies_persisted_nodes_and_viewport(monkeypatch):
    set_def = {
        "id": "set2",
        "states": [{"id": "S0"}],
        "transitions": [],
        "initial": "S0",
    }
    model = _StubModel()

    # Fake persistence payload
    fake_layouts = {
        "by_set": {
            "set2": {
                "nodes": {
                    "S0": {"x": 123, "y": 77}
                },
                "viewport": {
                    "zoom": 1.75,
                    "pan_x": 10.0,
                    "pan_y": -5.0,
                    "legend_collapsed": True,
                },
            }
        }
    }

    # Monkeypatch the imported functions inside the module
    monkeypatch.setattr(gb, "load_layouts", lambda path: fake_layouts, raising=True)
    monkeypatch.setattr(gb, "default_layouts_path", lambda: "__unused__", raising=True)

    nodes, edges = gb.build_graph_from_set(set_def, model, canvas=(400, 300))

    assert len(nodes) == 1 and nodes[0]["id"] == "S0"
    assert nodes[0]["x"] == 123 and nodes[0]["y"] == 77
    assert model.zoom == pytest.approx(1.75)
    assert model.pan_x == pytest.approx(10.0)
    assert model.pan_y == pytest.approx(-5.0)
    assert model.legend_collapsed is True
