import pytest
from roguelike_game.ecs.systems.combat.spells.spell_casting_system import SpellCastingSystem


def test_spell_casting_system_instantiation():
    sys = SpellCastingSystem(None)
    assert isinstance(sys, SpellCastingSystem), "Debe instanciar SpellCastingSystem correctamente"
