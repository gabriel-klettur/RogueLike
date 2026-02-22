import time
from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_perf_budget():
    buildings = [SimpleNamespace(update=lambda s, m: None) for _ in range(500)]
    t0 = time.perf_counter()
    BuildingsUpdater().update(buildings, state=None, game_map=None, perf_log=None)
    dt = time.perf_counter() - t0
    assert dt < 0.1
