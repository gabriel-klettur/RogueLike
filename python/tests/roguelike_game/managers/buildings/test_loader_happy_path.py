import types
from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_happy_path(monkeypatch):
    calls = {}

    def fake_load(z_state):
        calls["arg"] = z_state
        return ["b1", "b2"]

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fake_load,
        raising=True,
    )

    z_state = types.SimpleNamespace(seed=123)
    loader = BuildingsLoader()
    result = loader.load(z_state)

    assert result == ["b1", "b2"]
    assert calls["arg"] is z_state
