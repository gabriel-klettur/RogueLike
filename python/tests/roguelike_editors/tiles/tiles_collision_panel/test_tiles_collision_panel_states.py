import pytest
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_states import TilesCollisionPanelState

def test_default_state():
    state = TilesCollisionPanelState()
    assert state.open is False
    assert state.choice is None
    assert isinstance(state.option_rects, dict)
    assert state.option_rects == {}
