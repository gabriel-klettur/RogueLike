import dataclasses

from roguelike_game.ecs.components.experience_component import ExperienceComponent
from roguelike_game.ecs.components.input_component import InputComponent


def test_experience_component_defaults():
    xp = ExperienceComponent()
    assert xp.xp == 0
    assert xp.level == 0
    assert xp.xp_to_next_level == 100

    data = dataclasses.asdict(xp)
    assert data == {"xp": 0, "level": 0, "xp_to_next_level": 100}


def test_input_component_initial_flags():
    ic = InputComponent()
    # Movement
    assert ic.move_x == 0
    assert ic.move_y == 0
    # Combat and spells flags default to False/None
    assert ic.attack is False
    assert ic.spell_lightball is False
    assert ic.spell_slash is False
    assert ic.spell_healing_aura is False
    assert ic.spell_darkball is False
    assert ic.spell_iceball is False
    assert ic.spell_lightning is False
    assert ic.spell_arcane_flame is False
    assert ic.spell_firework_launch is False
    assert ic.spell_smoke is False
    assert ic.spell_smoke_emitter is False
    assert ic.spell_sphere_magic_shield is False
    assert ic.spell_teleport is False
    assert ic.click is False
    assert ic.drop is False
    assert ic.toggle_editor is False
    assert ic.toggle_inventory is False
    assert ic.interact is False
    assert ic.show_all_drops is False
    assert ic.use_item is None
    assert ic.ui_drag is False
