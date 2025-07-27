import pytest
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_state import TileToolbarState


def test_default_state_values():
    state = TileToolbarState()
    assert state.view_active is True
    assert state.layers_view_open is False
    assert isinstance(state.visible_layers, dict)
    for layer in Layer:
        assert layer in state.visible_layers
        assert state.visible_layers[layer] is True
    assert state.show_buildings is True
    assert state.show_collisions is False
    assert state.show_collisions_overlay is False
    assert state.collision_picker_open is False
    assert state.collision_choice is None
    assert state.pos is None
    assert state.dragging is False
    assert state.drag_offset == (0, 0)
    assert state.btn_delete_rect is None
    assert state.btn_default_rect is None
