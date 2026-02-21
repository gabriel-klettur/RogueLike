from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_ignores_objects_without_update():
    b_with = SimpleNamespace(update=lambda s, m: None)
    b_without = object()
    updater = BuildingsUpdater()
    # No debe lanzar excepción
    updater.update([b_with, b_without], state=None, game_map=None, perf_log=None)
