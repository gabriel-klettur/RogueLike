import pytest
from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_io_errors(monkeypatch):
    def fake_load(_):
        raise IOError("boom")

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fake_load,
        raising=True,
    )

    loader = BuildingsLoader()
    with pytest.raises(IOError):
        loader.load(object())
