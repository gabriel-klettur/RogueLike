from types import SimpleNamespace

import pytest

from roguelike_editors.entities.services import spawn_services


@pytest.fixture
def game_stub():
    # minimal game with ecs world
    return SimpleNamespace(ecs=SimpleNamespace(ecs_world=SimpleNamespace()))


def test_spawn_entity_uses_player_factory_for_known_player(game_stub, monkeypatch):
    calls = {"factory": None, "kwargs": None}

    class FakeFactory:
        def create(self, world, **kwargs):
            calls["kwargs"] = kwargs
            return 111

    def fake_get_factory(name):
        calls["factory"] = name
        return FakeFactory()

    monkeypatch.setattr(
        'roguelike_game.factories.registry.get_factory',
        fake_get_factory,
    )

    # player_stats contains the type, so this is a player spawn
    player_stats = {"player_knight": {}}
    eid = spawn_services.spawn_entity(game_stub, "player_knight", 3, 5, player_stats)

    assert eid == 111
    assert calls["factory"] == "player"
    assert calls["kwargs"] == {"tile_x": 3, "tile_y": 5, "class_player": "player_knight"}


def test_spawn_entity_uses_monster_factory_for_unknown_player(game_stub, monkeypatch):
    calls = {"factory": None, "kwargs": None}

    class FakeFactory:
        def create(self, world, **kwargs):
            calls["kwargs"] = kwargs
            return 222

    def fake_get_factory(name):
        calls["factory"] = name
        return FakeFactory()

    monkeypatch.setattr(
        'roguelike_game.factories.registry.get_factory',
        fake_get_factory,
    )

    # player_stats missing type => treat as monster
    player_stats = {"player_knight": {}}
    eid = spawn_services.spawn_entity(game_stub, "orc_grunt", 7, 9, player_stats)

    assert eid == 222
    assert calls["factory"] == "monster"
    assert calls["kwargs"] == {"tile_x": 7, "tile_y": 9, "monster_type": "orc_grunt"}
