import pytest

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent


def test_position_attributes():
    pos = Position(3.5, -2.0)
    assert pos.x == 3.5
    assert pos.y == -2.0
    # Attributes are mutable
    pos.x = 10
    pos.y = 20
    assert pos.x == 10
    assert pos.y == 20


def test_physical_item_component():
    comp = PhysicalItemComponent('drop1', 'itemA', 5, zone_id='ZoneX')
    assert comp.drop_id == 'drop1'
    assert comp.item_id == 'itemA'
    assert comp.quantity == 5
    assert comp.zone_id == 'ZoneX'


def test_physical_item_component_optional_zone():
    comp = PhysicalItemComponent('drop2', 'itemB', 1)
    assert comp.zone_id is None


def test_collectible_component():
    # Just ensure it can be instantiated and is of correct type
    comp = CollectibleComponent()
    assert isinstance(comp, CollectibleComponent)
