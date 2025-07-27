import pytest
from roguelike_editors.tiles.layers_panel.layers_panel_states import LayersPanelState


def test_default_state_values():
    state = LayersPanelState()
    assert state.visible_layers == {}
    assert state.option_rects == {}
    assert state.pos is None
    assert state.dragging is False
    assert state.drag_offset == (0, 0)
