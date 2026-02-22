import time
from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_perf_budget(monkeypatch):
    def fast_load(_):
        return [object() for _ in range(50)]

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fast_load,
        raising=True,
    )

    loader = BuildingsLoader()
    t0 = time.perf_counter()
    _ = loader.load(object())
    dt = time.perf_counter() - t0
    assert dt < 0.05
