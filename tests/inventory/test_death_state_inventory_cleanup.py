import json
import pytest
from pathlib import Path
from roguelike_game.ecs.systems.fsm.states.death_state import DeathState

class DummyWorld:
    def __init__(self):
        self.components = {'PlayerTagComponent': {}, 'DeathTimer': {}, 'GrayscaleComponent': {}}
    def remove_entity(self, eid):
        for comp in self.components.values():
            comp.pop(eid, None)

class DummyEntity:
    def __init__(self, eid, world):
        self.id = eid
        self.world = world

@pytest.fixture(autouse=True)
def chdir_tmp(monkeypatch, tmp_path):
    """Change working directory to a temp path for file isolation."""
    monkeypatch.chdir(tmp_path)
    # Ensure data directory exists
    data_dir = tmp_path / 'data'
    data_dir.mkdir()
    return tmp_path

def write_active_inventory(tmp_path, entry_id):
    inv = {str(entry_id): {'template_id': 't', 'slots': [], 'schema_version': '1.0.0'}}
    path = tmp_path / 'data' / 'inventory_monsters.json'
    with open(path, 'w') as f:
        json.dump(inv, f, indent=2)
    return path

def test_npc_inventory_entry_removed(tmp_path):
    # Setup active inventory with a NPC entry
    eid = 42
    inv_path = write_active_inventory(tmp_path, eid)

    # Simulate death
    world = DummyWorld()
    entity = DummyEntity(eid, world)
    death = DeathState()
    death.enter(entity)
    # Force elapsed >= duration by resetting start_time
    dt_cmp = world.components['DeathTimer'][eid]
    dt_cmp.start_time = 0

    death.execute(entity, 0)

    # File should no longer contain the entry
    data = json.loads(inv_path.read_text())
    assert str(eid) not in data, "NPC inventory entry was not removed"

def test_player_inventory_entry_not_removed(tmp_path):
    # Setup active inventory with a player entry
    eid = 7
    inv_path = write_active_inventory(tmp_path, eid)

    # Simulate death as player
    world = DummyWorld()
    world.components['PlayerTagComponent'][eid] = object()
    entity = DummyEntity(eid, world)
    death = DeathState()
    death.enter(entity)
    # Force elapsed >= duration
    dt_cmp = world.components['DeathTimer'][eid]
    dt_cmp.start_time = 0

    death.execute(entity, 0)

    # File should still contain the entry
    data = json.loads(inv_path.read_text())
    assert str(eid) in data, "Player inventory entry should not be removed"
