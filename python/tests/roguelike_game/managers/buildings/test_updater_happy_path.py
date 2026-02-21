from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_happy_path_calls_update_in_order():
    calls = []

    def make_b(name):
        return SimpleNamespace(
            name=name,
            update=lambda state, game_map: calls.append((name, state, game_map)),
        )

    b1 = make_b("b1")
    b2 = make_b("b2")
    updater = BuildingsUpdater()
    updater.update([b1, b2], state="S", game_map="M", perf_log=None)

    assert calls == [("b1", "S", "M"), ("b2", "S", "M")]
