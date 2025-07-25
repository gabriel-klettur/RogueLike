import pytest
from roguelike_editors.tiles.tiles_view_panel.tiles_view_state import TilesViewPanelState


def test_default_state_values():
    state = TilesViewPanelState()
    assert state.active is False
    assert state.pos is None
    assert state.dragging is False
    assert state.drag_offset == (0, 0)
    assert state.size is None
