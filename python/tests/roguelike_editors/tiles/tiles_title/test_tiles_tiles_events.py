import pytest
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_title.tiles_tiles_events import TilesTitleEventHandler


def test_init_sets_controller_and_state():
    ctrl = SimpleNamespace()
    state = SimpleNamespace()
    handler = TilesTitleEventHandler(ctrl, state)
    assert handler.controller is ctrl
    assert handler.state is state


def test_handle_event_returns_none_for_unknown_event():
    handler = TilesTitleEventHandler(None, None)
    result = handler.handle_event(ev=object())
    assert result is None
