import time
import dataclasses
import pytest

from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.core.npc_tag import NPCTagComponent


def test_identity_construction_and_fields():
    ident = Identity(id=42, name="Hero", title="Slayer", faction=Faction.GOOD)
    assert ident.id == 42
    assert ident.name == "Hero"
    assert ident.title == "Slayer"
    assert ident.faction is Faction.GOOD


def test_identity_enum_members():
    # Ensure expected enum members exist and are unique
    values = {Faction.GOOD.value, Faction.NEUTRAL.value, Faction.EVIL.value}
    assert values == {"good", "neutral", "evil"}


def test_identity_serialization_with_asdict():
    ident = Identity(id=1, name="NPC", title="Vendor", faction=Faction.NEUTRAL)
    data = dataclasses.asdict(ident)
    assert data == {
        "id": 1,
        "name": "NPC",
        "title": "Vendor",
        "faction": Faction.NEUTRAL,
    }


def test_player_tag_defaults_and_custom_class():
    default_tag = PlayerTagComponent()
    assert default_tag.class_name is None

    mage_tag = PlayerTagComponent(class_name="Mage")
    assert mage_tag.class_name == "Mage"


def test_npc_tag_instantiation_has_no_extra_state():
    npc = NPCTagComponent()
    # Component is intentionally empty; its instance dict should be empty
    assert isinstance(npc, NPCTagComponent)
    assert vars(npc) == {}
