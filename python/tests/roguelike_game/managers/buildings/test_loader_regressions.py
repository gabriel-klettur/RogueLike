from types import SimpleNamespace
from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_regressions_idempotent_calls(monkeypatch):
    # Cada llamada debe delegar al loader subyacente (no cachea por sí mismo)
    calls = {"n": 0}

    def fake_load(_):
        calls["n"] += 1
        return [SimpleNamespace(i=calls["n"])]

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fake_load,
        raising=True,
    )

    loader = BuildingsLoader()
    a = loader.load(object())
    b = loader.load(object())
    assert a != b
    assert calls["n"] == 2
