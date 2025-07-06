import json
import pytest

from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.physical_item_component import PhysicalItemComponent
from roguelike_game.ecs.components.collectible_component import CollectibleComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.position import Position

class DummyWorld:
    def __init__(self):
        self.components = {}
        self._removed = []
    def remove_entity(self, eid):
        self._removed.append(eid)

@pytest.fixture
def tmp_files(tmp_path):
    monsters = tmp_path / 'monsters.json'
    players = tmp_path / 'players.json'
    drops = tmp_path / 'drops.json'
    monsters.write_text(json.dumps({'1': {'slots': [None]}}))
    players.write_text(json.dumps({'1': {'slots': [None]}}))
    return str(monsters), str(players), str(drops)


def test_pickup_adds_to_inventory_and_removes_drop(tmp_files, monkeypatch):
    monsters_path, players_path, drops_path = tmp_files
    system = InventoryPickupSystem(
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    inv = InventoryComponent(capacity=2)
    inp = InputComponent()
    inp.click = True
    # Create physical item entity
    phys = PhysicalItemComponent(drop_id='d1', item_id='itemB', quantity=2)
    pos_player = Position(x=5, y=5)
    pos_drop = Position(x=6, y=6)

    world.components['InventoryComponent'] = {1: inv}
    world.components['PhysicalItemComponent'] = {2: phys}
    world.components['CollectibleComponent'] = {2: CollectibleComponent()}
    world.components['InputComponent'] = {1: inp}
    world.components['Position'] = {1: pos_player, 2: pos_drop}

    # Populate drop JSON
    with open(drops_path, 'w', encoding='utf-8') as f:
        json.dump({'d1': {'item_id': 'itemB', 'quantity': 2, 'zone_id': 'z', 'position': {'x': 6, 'y': 6}}}, f)

    system.update(world)

    assert inv.has('itemB', 2)
    assert 2 in world._removed
    data = json.loads(open(drops_path, encoding='utf-8').read())
    # Drop entry removed
    assert 'd1' not in data

@pytest.mark.parametrize('close_enough', [True, False])
def test_pickup_requires_proximity(tmp_files, close_enough):
    monsters_path, players_path, drops_path = tmp_files
    system = InventoryPickupSystem(
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    inv = InventoryComponent(capacity=2)
    inp = InputComponent()
    inp.click = True
    phys = PhysicalItemComponent(drop_id='d2', item_id='itemC', quantity=1)
    
    if close_enough:
        pos_player = Position(x=0, y=0)
        pos_drop = Position(x=0, y=0)
    else:
        pos_player = Position(x=0, y=0)
        pos_drop = Position(x=1000, y=1000)

    world.components['InventoryComponent'] = {1: inv}
    world.components['PhysicalItemComponent'] = {2: phys}
    world.components['CollectibleComponent'] = {2: CollectibleComponent()}
    world.components['InputComponent'] = {1: inp}
    world.components['Position'] = {1: pos_player, 2: pos_drop}

    # Create drop file
    with open(drops_path, 'w', encoding='utf-8') as f:
        json.dump({'d2': {'item_id': 'itemC', 'quantity': 1, 'zone_id': 'z', 'position': {'x': pos_drop.x, 'y': pos_drop.y}}}, f)

    system.update(world)

    data = json.loads(open(drops_path, encoding='utf-8').read())
    if close_enough:
        assert inv.has('itemC', 1)
        assert 2 in world._removed
        assert 'd2' not in data
    else:
        assert not inv.has('itemC', 1)
        # Drop remains
        assert 'd2' in data
