import pytest
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.systems.physics.movement_collision_system import MovementCollisionSystem


def test_entity_moves_without_obstacles(world):
    eid = world.create_entity()
    world.components['Position'][eid] = Position(0, 0)
    world.components['Velocity'][eid] = Velocity(5, 0)
    world.components['MultiCollider'][eid] = MultiCollider({'feet': Collider(10, 10)})
    # No solid tiles en dummy_map (sin obstáculos)
    MovementCollisionSystem(None).update(world)
    assert world.components['Position'][eid].x == 5, "La entidad debe moverse según su velocidad en X"
