import pytest
from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_bubbles_errors_and_stops_iteration():
    called = {"b2": False}

    def bad_update(state, game_map):
        raise RuntimeError("fail")

    def ok_update(state, game_map):
        called["b2"] = True

    b1 = SimpleNamespace(update=bad_update)
    b2 = SimpleNamespace(update=ok_update)

    updater = BuildingsUpdater()
    with pytest.raises(RuntimeError):
        updater.update([b1, b2], state=None, game_map=None, perf_log=None)

    assert called["b2"] is False
