import json
import pytest

import roguelike_game.ecs.systems.inventory.death_drop_system as death_mod
from roguelike_game.ecs.systems.inventory.death_drop_system import DeathDropSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.transform.position import Position

class DummyWorld:
    def __init__(self):
        self.components = {}

@pytest.fixture
def tmp_files(tmp_path):
    monsters = tmp_path / 'monsters.json'
    players = tmp_path / 'players.json'
    drops = tmp_path / 'drops.json'
    initial_monsters = {'1': {'slots': [None, None], 'template_id': 't1'}}
    initial_players = {'1': {'slots': [None, None], 'player_id': 'p1'}}
    monsters.write_text(json.dumps(initial_monsters))
    players.write_text(json.dumps(initial_players))
    return str(monsters), str(players), str(drops)

def test_death_drop_persist_and_create_drops(tmp_files, monkeypatch):
    monsters_path, players_path, drops_path = tmp_files
    # Patch zone calculation
    monkeypatch.setattr(death_mod, 'get_zone_for_tile', lambda tx, ty: 'zoneXYZ')
    system = DeathDropSystem(
        perf_log=None,
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    eid = 1
    inv = InventoryComponent(capacity=2, player_id='t1')
    inv.add('itemX', 5)
    pos = Position(32, 64)
    from roguelike_game.ecs.components.combat.death_timer import DeathTimer
    dt = DeathTimer(start_time=0)
    world.components = {
        'InventoryComponent': {eid: inv},
        'Position': {eid: pos},
        'DeathTimer': {eid: dt}
    }
    # Run update
    system.update(world)
    # Verify drop file
    drop_data = json.loads(open(drops_path, encoding='utf-8').read())
    assert len(drop_data) == 1
    drop_entry = next(iter(drop_data.values()))
    assert drop_entry['item_id'] == 'itemX'
    assert drop_entry['quantity'] == 5
    assert drop_entry['zone_id'] == 'zoneXYZ'
    assert drop_entry['position'] == {'x': 32, 'y': 64}
    # Verify monster inventory persisted empty
    mon_data = json.loads(open(monsters_path, encoding='utf-8').read())
    assert mon_data['1']['slots'] == [None, None]
    # Verify player inventory persisted empty
    pl_data = json.loads(open(players_path, encoding='utf-8').read())
    assert pl_data['1']['slots'] == [None, None]
    # Verify inventory component slots cleared
    assert all(slot is None for slot in inv.slots)

@pytest.mark.parametrize('has_inv, has_pos, has_death', [
    (False, True, True),
    (True, False, True),
    (True, True, False),
])
def test_death_drop_skipped_on_missing_components(tmp_files, has_inv, has_pos, has_death, monkeypatch):
    monsters_path, players_path, drops_path = tmp_files
    system = DeathDropSystem(
        perf_log=None,
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    eid = 1
    inv = InventoryComponent(capacity=1, player_id='t1')
    pos = Position(0, 0)
    from roguelike_game.ecs.components.combat.death_timer import DeathTimer
    dt = DeathTimer(start_time=0)
    # Conditionally populate components
    if has_inv:
        world.components.setdefault('InventoryComponent', {})[eid] = inv
    if has_pos:
        world.components.setdefault('Position', {})[eid] = pos
    if has_death:
        world.components.setdefault('DeathTimer', {})[eid] = dt
    # Should not raise
    system.update(world)
    # Drop file empty or not created
    try:
        data = json.loads(open(drops_path, encoding='utf-8').read())
    except FileNotFoundError:
        data = {}
    assert data == {}
    # Monster and player JSON remain unchanged
    mon_data = json.loads(open(monsters_path, encoding='utf-8').read())
    assert mon_data['1']['slots'] == [None, None]
    pl_data = json.loads(open(players_path, encoding='utf-8').read())
    assert pl_data['1']['slots'] == [None, None]
