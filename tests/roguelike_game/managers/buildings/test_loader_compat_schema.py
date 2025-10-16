from types import SimpleNamespace
from roguelike_game.managers.buildings.loader import BuildingsLoader


def test_loader_compat_schema(monkeypatch):
    # Simula que el loader subyacente devuelve objetos "compatibles" (duck typing)
    buildings = [SimpleNamespace(id=i) for i in range(3)]

    def fake_load(_):
        return buildings

    monkeypatch.setattr(
        "roguelike_game.managers.buildings.loader.load_buildings_from_json",
        fake_load,
        raising=True,
    )

    out = BuildingsLoader().load(object())
    assert isinstance(out, list)
    assert out == buildings
