from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_preserves_order_of_updates():
    order = []
    b1 = SimpleNamespace(update=lambda s, m: order.append("b1"))
    b2 = SimpleNamespace(update=lambda s, m: order.append("b2"))
    BuildingsUpdater().update([b1, b2], state=None, game_map=None, perf_log=None)
    assert order == ["b1", "b2"]
