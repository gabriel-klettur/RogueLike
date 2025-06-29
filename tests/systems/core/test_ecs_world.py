import pytest
from roguelike_game.ecs.core.manager import ECSWorld
import roguelike_game.ecs.core.manager as mgr_mod

class DummySpatialIndex:
    def __init__(self, map_manager, buildings):
        pass


def test_ecsworld_entity_lifecycle(monkeypatch):
    # Stub external dependencies
    monkeypatch.setattr(mgr_mod, 'SpatialIndex', DummySpatialIndex)
    monkeypatch.setattr(mgr_mod, 'get_update_system_classes', lambda: [])
    monkeypatch.setattr(mgr_mod, 'get_render_system_classes', lambda: [])
    # Initialize world
    world = ECSWorld(screen=None, map_manager=None, buildings=None)
    # Test entity creation
    assert world.entities == []
    eid1 = world.create_entity()
    eid2 = world.create_entity()
    assert eid1 != eid2
    assert eid1 in world.entities and eid2 in world.entities
    # Add dummy components
    world.components.setdefault('CompA', {})[eid1] = object()
    world.components.setdefault('CompA', {})[eid2] = object()
    world.components.setdefault('CompB', {})[eid2] = object()
    # Query entities by component
    assert set(world.get_entities_with('CompA')) == {eid1, eid2}
    assert list(world.get_entities_with('CompA', 'CompB')) == [eid2]
    # Remove entity and ensure cleanup
    world.remove_entity(eid1)
    assert eid1 not in world.entities
    assert eid1 not in world.components['CompA']
