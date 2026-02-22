import pytest
import pygame
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_state import TilePickerState

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_default_state_values():
    state = TilePickerState()
    assert state.open is False
    assert state.current_choice is None
    assert state.scroll_offset == 0
    assert state.pos is None
    assert state.dragging is False
    assert state.drag_offset == (0, 0)
    assert state.surface is None
    assert state.btn_close_rect is None
    assert state.tileset_filter is False
    assert state.tileset_grid_size_text == "32"
    assert state.tileset_grid_size == 32
    assert state.tileset_input_active is False
    assert state.tileset_input_rect is None
    assert state.tileset_checkbox_rect is None
    assert state.btn_tileset_rect is None
    assert state.tileset_source is None
    assert state.btn_config_rect is None
    assert state.config_mode is False
    assert state.config_src_idx is None
