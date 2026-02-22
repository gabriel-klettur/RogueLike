from roguelike_game.ecs.systems.input.helpers import map_keyboard_spells
from roguelike_game.ecs.systems.input.constants import SPELL_ATTRS


def test_puddle_lava_in_spell_attrs_and_mapping():
    assert 'puddle_lava' in SPELL_ATTRS

    class Inp:
        spell_puddle_lava = False

    calls = {}

    def any_pressed(name: str) -> bool:
        calls[name] = calls.get(name, 0) + 1
        return name == 'spell_puddle_lava'

    inp = Inp()
    map_keyboard_spells(inp, any_pressed)

    assert inp.spell_puddle_lava is True
    assert calls.get('spell_puddle_lava', 0) == 1
