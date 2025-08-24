from __future__ import annotations

from roguelike_editors.fsm.services import fsm_id as fid


def test_new_id_increments_from_1():
    existing = {"node_1", "node_2"}
    assert fid.new_id("node", existing) == "node_3"


def test_new_id_fills_gaps():
    existing = {"node_1", "node_3"}
    assert fid.new_id("node", existing) == "node_2"
