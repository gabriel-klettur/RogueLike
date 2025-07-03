import pytest
from roguelike_game.factories.registry import get_factory
from roguelike_game.factories.monster.builder import MonsterBuilder

from roguelike_game.factories.monster.facade import MonsterFactory

def test_get_factory_returns_monster_factory():
    factory = get_factory("monster")
    assert isinstance(factory, MonsterFactory)

def test_create_with_xy(monkeypatch, world):
    called = {}
    monkeypatch.setattr(MonsterBuilder, "build", lambda self, x, y, monster_type: (called.setdefault('args', (x, y, monster_type)), 99)[1])
    factory = get_factory("monster")
    eid = factory.create(world, x=10, y=20, monster_type="dummy")
    assert eid == 99
    assert called['args'] == (10, 20, "dummy")

def test_create_with_tile(monkeypatch, world):
    import roguelike_game.factories.monster.facade as facade_mod
    monkeypatch.setattr(facade_mod, "calibrate_tile_position", lambda tx, ty, mt: (tx*2, ty*3))
    called = {}
    monkeypatch.setattr(MonsterBuilder, "build", lambda self, x, y, monster_type: (called.setdefault('args', (x, y, monster_type)), 42)[1])
    factory = get_factory("monster")
    eid = factory.create(world, tile_x=3, tile_y=4, monster_type="dummy")
    assert eid == 42
    assert called['args'] == (6, 12, "dummy")
