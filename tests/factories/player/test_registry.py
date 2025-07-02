import pytest
from roguelike_game.factories.registry import get_factory
from roguelike_game.factories.player.facade import PlayerFactory


def test_get_factory_player():
    factory = get_factory("player")
    assert isinstance(factory, PlayerFactory)


def test_get_factory_invalid():
    with pytest.raises(KeyError):
        get_factory("not_a_factory")
