import dataclasses

from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.experience_component import ExperienceComponent


def test_identity_asdict_serialization():
    ident = Identity(id=2, name="Ally", title="Guard", faction=Faction.NEUTRAL)
    data = dataclasses.asdict(ident)
    assert data == {"id": 2, "name": "Ally", "title": "Guard", "faction": Faction.NEUTRAL}


def test_experience_component_asdict():
    xp = ExperienceComponent(xp=10, level=1, xp_to_next_level=50)
    data = dataclasses.asdict(xp)
    assert data == {"xp": 10, "level": 1, "xp_to_next_level": 50}
