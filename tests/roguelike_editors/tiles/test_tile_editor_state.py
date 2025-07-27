import pytest
from roguelike_editors.tiles.tile_editor_state import TileEditorState
from roguelike_engine.map.model.layer import Layer


def test_defaults():
    state = TileEditorState()
    assert state.active is False
    assert state.selected_tile is None
    assert state.current_choice is None
    assert state.scroll_offset == 0
    assert state.current_tool == "select"
    assert state.brush_dragging is False
    assert state.current_layer == Layer.Ground
    # panel states exist
    assert hasattr(state, 'toolbar_state')
    assert hasattr(state, 'picker_state')
    assert hasattr(state, 'view_panel_state')
    assert hasattr(state, 'title_state')
    assert hasattr(state, 'collision_panel_state')
    assert hasattr(state, 'layers_panel_state')
    assert hasattr(state, 'size_panel_state')
    assert state.eyedropper_flash_start is None


def test_clone():
    state = TileEditorState()
    state.current_choice = "choice"
    clone = state.clone()
    assert clone is not state
    assert clone.current_choice == state.current_choice
    # nested states are deep-copied
    assert clone.toolbar_state is not state.toolbar_state
