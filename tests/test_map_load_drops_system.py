import pytest
from types import SimpleNamespace

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.utils import map_utils
import roguelike_game.ecs.systems.inventory.map_load_drops_system as mlds

class DummyMapManager:
    def get_spawn_pixel(self, tile_coords):
        # Simple: pixel coords = tile_coords * TILE_SIZE
        return tile_coords[0] * TILE_SIZE, tile_coords[1] * TILE_SIZE

class DummyWorld:
    def __init__(self):
        self.map_manager = DummyMapManager()
        self.components = {
            'PhysicalItemComponent': {},
            'Position': {},
            'CollectibleComponent': {}
        }
        self._next_eid = 1

    def create_entity(self):
        eid = self._next_eid
        self._next_eid += 1
        return eid

@pytest.fixture(autouse=True)
def suppress_persistence(monkeypatch):
    # Prevent actual file writes in ItemDropManager
    monkeypatch.setattr(
        'roguelike_game.managers.map.item_drop_manager.ItemDropManager._persist',
        lambda self: None
    )
    return monkeypatch


def test_map_load_drops_system_tile(monkeypatch):
    system = MapLoadDropsSystem()
    # Setup test data with tile
    system.drop_manager._data = {
        'd1': {
            'item_id': 'gold',
            'quantity': 2,
            'zone_id': 'Z',
            'tile': {'x': 1, 'y': 2}
        }
    }
    # Monkeypatch zone offset
    monkeypatch.setattr(mlds, 'get_zone_offset', lambda z: (5, 6))
    world = DummyWorld()

    system.update(world)

    # Should load exactly one entity
    assert system._loaded is True
    phys_map = world.components['PhysicalItemComponent']
    assert len(phys_map) == 1
    eid, phys = next(iter(phys_map.items()))
    assert isinstance(phys, PhysicalItemComponent)
    assert phys.drop_id == 'd1'
    assert phys.item_id == 'gold'
    assert phys.quantity == 2
    assert phys.zone_id == 'Z'

    pos = world.components['Position'][eid]
    assert isinstance(pos, Position)
    # offset (5,6) + tile (1,2) => global tile (6,8)
    assert pos.x == 6 * TILE_SIZE
    assert pos.y == 8 * TILE_SIZE
    # CollectibleComponent exists
    assert isinstance(world.components['CollectibleComponent'][eid], CollectibleComponent)

    # Second update should not add new entities
    system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 1


def test_map_load_drops_system_position(monkeypatch):
    system = MapLoadDropsSystem()
    # Setup test data with position
    system.drop_manager._data = {
        'd2': {
            'item_id': 'silver',
            'quantity': 3,
            'zone_id': 'Z',
            'position': {'x': 10, 'y': 20}
        }
    }
    monkeypatch.setattr(mlds, 'get_zone_offset', lambda z: (1, 2))
    world = DummyWorld()

    system.update(world)

    phys_map = world.components['PhysicalItemComponent']
    assert len(phys_map) == 1
    eid = next(iter(phys_map))
    pos = world.components['Position'][eid]
    # offset (1,2) tiles => pixel offset = (32,64)
    assert pos.x == 10 + 1 * TILE_SIZE
    assert pos.y == 20 + 2 * TILE_SIZE
