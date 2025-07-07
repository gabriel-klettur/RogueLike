import json
import tempfile
import pytest

from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.ecs.systems.inventory.death_drop_system import DeathDropSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.item_models import ItemStack
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.death_timer import DeathTimer

# Dummy world for testing
class DummyMapManager:
    def get_spawn_pixel(self, coords):
        return coords

class DummyWorld:
    def __init__(self):
        from collections import defaultdict
        self.components = defaultdict(dict)
        self.map_manager = DummyMapManager()
        self._next_id = 1
    def create_entity(self):
        eid = self._next_id
        self._next_id += 1
        return eid

@pytest.fixture(autouse=True)
def clean_temp_inventory(tmp_path, monkeypatch):
    # Ensure DeathDropSystem and MapLoadDropsSystem use tmp JSON files
    # Override default data paths to tmp files
    empty = {}
    # Create empty inventory_map.json
    inv_map = tmp_path / "drops.json"
    inv_map.write_text(json.dumps(empty))
    # Monkeypatch paths
    monkeypatch.setenv('ROGUELIKE_DATA', str(tmp_path))
    return inv_map

@pytest.fixture
def map_system(tmp_path, clean_temp_inventory):
    # instantiate MapLoadDropsSystem with custom drop file
    from roguelike_game.managers.map.item_drop_manager import ItemDropManager
    ms = MapLoadDropsSystem(None)
    ms.drop_manager = ItemDropManager(str(clean_temp_inventory))
    ms.items = {}  # no sprite models
    return ms

@pytest.fixture
def death_system(tmp_path, clean_temp_inventory):
    # instantiate DeathDropSystem with custom drop file
    ds = DeathDropSystem(None, drop_path=str(clean_temp_inventory))
    return ds


def test_map_load_drops_incremental(map_system, clean_temp_inventory):
    # initial drops
    initial = {
        "d1": {"item_id": "gold", "quantity": 1, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 5, "y": 5}},
        "d2": {"item_id": "wood", "quantity": 2, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 10, "y": 10}}
    }
    clean_temp_inventory.write_text(json.dumps(initial))
    world = DummyWorld()
    # first update: spawn both
    map_system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 2
    # second update: no duplicates
    map_system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 2
    # add new drop
    updated = dict(initial)
    updated["d3"] = {"item_id": "orb", "quantity": 3, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 20, "y": 20}}
    clean_temp_inventory.write_text(json.dumps(updated))
    map_system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 3
    # test invalid JSON does not raise
    clean_temp_inventory.write_text("INVALID JSON")
    map_system.update(world)


def test_death_drop_immediate_spawn(map_system, death_system):
    world = DummyWorld()
    # prepare entity
    eid = world.create_entity()
    # set inventory with one ItemStack
    inv = InventoryComponent(capacity=1)
    inv.slots[0] = ItemStack(item_id='gold', quantity=5)
    world.components['InventoryComponent'][eid] = inv
    # set position
    pos = Position(x=1.0, y=2.0)
    world.components['Position'][eid] = pos
    # mark as dead
    world.components['DeathTimer'][eid] = DeathTimer(0.0)

    # perform death drop
    death_system.update(world)

    # immediate spawn via map_system
    # catch new drop id
    physical_before = len(world.components['PhysicalItemComponent'])
    map_system.update(world)
    assert len(world.components['PhysicalItemComponent']) == physical_before + 1
    # check component data
    comps = world.components['PhysicalItemComponent']
    comp = next(iter(comps.values()))
    assert comp.item_id == 'gold'
    assert comp.quantity == 5
