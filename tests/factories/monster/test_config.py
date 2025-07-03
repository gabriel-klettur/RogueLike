import pytest
from roguelike_game.factories.monster.config import MONSTER_DEFS

def test_monster_defs_not_empty():
    assert isinstance(MONSTER_DEFS, dict)
    assert MONSTER_DEFS, "MONSTER_DEFS should not be empty"
