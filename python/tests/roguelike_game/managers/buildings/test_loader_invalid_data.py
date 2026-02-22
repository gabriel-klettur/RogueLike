from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_invalid_data_pass_through(monkeypatch):
    invalid = {"not": "a list"}

    def fake_load(_):
        return invalid

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fake_load,
        raising=True,
    )

    loader = BuildingsLoader()
    out = loader.load(object())
    assert out is invalid
