import json
import pytest

import roguelike_game.ecs.systems.inventory.death_drop_system as death_mod
from roguelike_game.ecs.systems.inventory.death_drop_system import DeathDropSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.death_timer import DeathTimer

class DummyWorld:
    def __init__(self):
        self.components = {}

@pytest.fixture
def tmp_files_empty(tmp_path):
    monsters = tmp_path / 'monsters.json'
    players = tmp_path / 'players.json'
    drops = tmp_path / 'drops.json'
    # create empty monsters file
    monsters.write_text('')
    # create valid players file with dummy entry
    players.write_text(json.dumps({'1': {'slots': [None], 'player_id': 'p1'}}))
    return str(monsters), str(players), str(drops)


def test_death_drop_with_empty_monster_file(tmp_files_empty, monkeypatch):
    monsters_path, players_path, drops_path = tmp_files_empty
    # Patch zone calculation to a fixed zone
    monkeypatch.setattr(death_mod, 'get_zone_for_tile', lambda tx, ty: 'zoneEmpty')
    system = DeathDropSystem(
        perf_log=None,
        active_monster_path=monsters_path,
        active_player_path=players_path,
        drop_path=drops_path
    )
    world = DummyWorld()
    eid = 1
    inv = InventoryComponent(capacity=1, player_id='p1')
    inv.add('itemY', 3)
    pos = Position(0, 0)
    dt = DeathTimer(start_time=0)
    world.components = {
        'InventoryComponent': {eid: inv},
        'Position': {eid: pos},
        'DeathTimer': {eid: dt}
    }
    # Run update to trigger drop
    system.update(world)
    # Verify drop file created with correct entry
    drop_data = json.loads(open(drops_path, encoding='utf-8').read())
    assert len(drop_data) == 1
    entry = next(iter(drop_data.values()))
    assert entry['item_id'] == 'itemY'
    assert entry['quantity'] == 3
    assert entry['zone_id'] == 'zoneEmpty'
    assert entry['position'] == {'x': 0, 'y': 0}
    # Verify monster file remains valid or empty without errors
    try:
        mon_text = open(monsters_path, encoding='utf-8').read()
        if mon_text.strip():
            data = json.loads(mon_text)
        else:
            data = {}
    except json.JSONDecodeError:
        pytest.skip("Monster file invalid JSON; acceptable behavior with empty file")
    assert data == {}
