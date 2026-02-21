from types import SimpleNamespace
from roguelike_game.managers.buildings.updater import BuildingsUpdater


def test_updater_accepts_duck_typed_objects():
    # Cualquier objeto con atributo callable 'update(state, game_map)' es válido
    obj = SimpleNamespace(update=lambda state, game_map: None)
    BuildingsUpdater().update([obj], state="S", game_map="M", perf_log=None)
