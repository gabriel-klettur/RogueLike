import pytest
from roguelike_editors.tiles.tiles_title.tiles_tiles_states import TilesTitleState


def test_default_title():
    state = TilesTitleState()
    assert state.title == ""
    # Title can be updated
    state.title = "New Title"
    assert state.title == "New Title"
