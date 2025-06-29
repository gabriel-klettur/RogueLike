# Path: tests/core/test_ecs_manager.py
import pytest
from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity


def test_create_and_remove_entity(world):
    eid = world.create_entity()
    assert eid in world.entities
    world.remove_entity(eid)
    assert eid not in world.entities


def test_get_entities_with(world):
    eid1 = world.create_entity()
    eid2 = world.create_entity()
    world.components['Position'][eid1] = Position(1, 1)
    world.components['Position'][eid2] = Position(2, 2)
    world.components['Velocity'][eid1] = Velocity(0, 0)
    result = list(world.get_entities_with('Position', 'Velocity'))
    assert result == [eid1]


def test_get_entities_in_camera(world, camera):
    eid = world.create_entity()
    world.components['Position'][eid] = Position(10, 10)
    result = list(world.get_entities_in_camera(camera, 'Position'))
    assert eid in result