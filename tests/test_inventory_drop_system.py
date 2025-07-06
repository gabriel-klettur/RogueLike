import json
import pytest

import roguelike_game.ecs.systems.inventory.inventory_drop_system as drop_mod
from roguelike_game.ecs.systems.inventory.inventory_drop_system import InventoryDropSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.position import Position

class DummyWorld:
    def __init__(self):
        self.components = {}

@pytest.fixture
def tmp_files(tmp_path, monkeypatch):
    # Prepare JSON files for persistence and drop path
    monsters = tmp_path / 'monsters.json'
    players = tmp_path / 'players.json'
    drops = tmp_path / 'drops.json'
    # Initialize inventory files with mapping for entity 1
    monsters.write_text(json.dumps({'1': {'slots': [None]}}))
    players.write_text(json.dumps({'1': {'slots': [None]}}))
    # Ensure drop file is created by manager
    return str(monsters), str(players), str(drops)

def test_drop_creates_map_drop_and_removes_item(tmp_files, monkeypatch):
    monsters_path, players_path, drops_path = tmp_files
    # Patch get_zone_for_tile
    monkeypatch.setattr(drop_mod, 'get_zone_for_tile', lambda tx, ty: 'zoneX')

    system = InventoryDropSystem(
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    # Set up inventory with one item
    inv = InventoryComponent(capacity=2)
    inv.add('itemA', 3)
    inp = InputComponent()
    inp.drop = True
    pos = Position(x=5, y=10)
    world.components['InventoryComponent'] = {1: inv}
    world.components['InputComponent'] = {1: inp}
    world.components['Position'] = {1: pos}

    system.update(world)

    # Inventory should have removed the item stack
    assert not inv.has('itemA', 1)
    # Drop file should contain the drop entry
    data = json.loads(open(drops_path, encoding='utf-8').read())
    assert len(data) == 1
    drop_id, entry = next(iter(data.items()))
    assert entry['item_id'] == 'itemA'
    assert entry['quantity'] == 3
    assert entry['zone_id'] == 'zoneX'
    assert entry['position'] == {'x': 5, 'y': 10}
    # Input flag reset
    assert inp.drop is False

@pytest.mark.parametrize('has_inv, has_pos', [(False, True), (True, False)])
def test_drop_skipped_when_missing_components(tmp_files, has_inv, has_pos):
    monsters_path, players_path, drops_path = tmp_files
    system = InventoryDropSystem(
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    inv = InventoryComponent(capacity=1)
    inp = InputComponent()
    inp.drop = True
    pos = Position(x=0, y=0)
    if has_inv:
        world.components['InventoryComponent'] = {1: inv}
    if has_pos:
        world.components['Position'] = {1: pos}
    world.components['InputComponent'] = {1: inp}

    # Should not raise and drop file remains empty or created
    system.update(world)
    data = json.loads(open(drops_path, encoding='utf-8').read())
    assert data == {}
