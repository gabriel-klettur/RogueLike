# Path: tests/systems/combat/spells/test_fireball_system.py
import pytest
from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem


def test_fireball_system_instantiation():
    sys = FireballSystem(None)
    assert isinstance(sys, FireballSystem), "Debe instanciar FireballSystem correctamente"