import json
import pytest

from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.systems.inventory.inventory_transfer_system import InventoryTransferSystem

class DummyWorld:
    def __init__(self):
        self.components = {}

@pytest.fixture
def tmp_files(tmp_path):
    # Prepare JSON files with initial keys for persistence
    monsters_file = tmp_path / 'monsters.json'
    players_file = tmp_path / 'players.json'
    monsters_file.write_text(json.dumps({'1': {'slots': [None]}}))
    players_file.write_text(json.dumps({'2': {'slots': [None]}}))
    return str(monsters_file), str(players_file)


def test_successful_transfer(tmp_files):
    monsters_path, players_path = tmp_files
    system = InventoryTransferSystem(active_monster_path=monsters_path, active_player_path=players_path)
    world = DummyWorld()
    inv1 = InventoryComponent(capacity=2)
    inv2 = InventoryComponent(capacity=2)
    inv1.add('item', 1)
    world.components['InventoryComponent'] = {1: inv1, 2: inv2}

    system.transfer(world, 'item', 1, 1, 2)

    # Source should have no items, target should have the item
    assert not inv1.has('item', 1)
    assert inv2.has('item', 1)

    # Persistence files updated
    monsters_data = json.loads(open(monsters_path, encoding='utf-8').read())
    players_data = json.loads(open(players_path, encoding='utf-8').read())
    assert monsters_data['1']['slots'] == inv1.serialize()['slots']
    assert players_data['2']['slots'] == inv2.serialize()['slots']


def test_transfer_no_space(tmp_files):
    monsters_path, players_path = tmp_files
    system = InventoryTransferSystem(active_monster_path=monsters_path, active_player_path=players_path)
    world = DummyWorld()
    # Capacity 1, target already full
    inv1 = InventoryComponent(capacity=1)
    inv2 = InventoryComponent(capacity=1)
    inv1.add('item', 1)
    inv2.add('other', 1)
    world.components['InventoryComponent'] = {1: inv1, 2: inv2}

    with pytest.raises(ValueError):
        system.transfer(world, 'item', 1, 1, 2)
    # Ensure rollback: source still has item
    assert inv1.has('item', 1)


def test_update_noop(tmp_files):
    monsters_path, players_path = tmp_files
    system = InventoryTransferSystem(active_monster_path=monsters_path, active_player_path=players_path)
    world = DummyWorld()
    # Debería existir el método update y no lanzar
    assert hasattr(system, 'update')
    system.update(world)
