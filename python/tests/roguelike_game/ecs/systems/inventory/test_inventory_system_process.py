import types
import uuid

import roguelike_game.ecs.systems.inventory.inventory_update_runner as invrun
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def test_run_inventory_init_creates_player_inventory_and_writes(tmp_path, monkeypatch):
    writes = []
    monkeypatch.setattr(invrun, 'write_json', lambda path, payload: writes.append((path, payload)), raising=True)

    # System stub with empty active stores and defaults
    system = types.SimpleNamespace(
        active_monsters={},
        active_players={},
        active_neutrals={},
        dirty_monsters=False,
        dirty_players=False,
        dirty_neutrals=False,
        player_template={"capacity": 12},
        schema_version="1.0.0",
        active_monster_path=str(tmp_path / 'monsters.json'),
        active_player_path=str(tmp_path / 'players.json'),
        active_neutral_path=str(tmp_path / 'neutrals.json'),
        initialized=set(),
        neutral_templates={},
        monster_templates={},
        vendor_support=types.SimpleNamespace(try_build_inventory_from_seed=lambda *a, **k: None),
    )

    # World with one player and empty stores
    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {1: object()},
        'NPCTagComponent': {},
        'MonsterInstanceComponent': {},
        'InventoryComponent': {},
        'ExperienceComponent': {},
    }

    invrun.run_inventory_init_update(system, world)

    # Inventory created with template capacity and a valid player_id
    inv = world.components['InventoryComponent'][1]
    assert isinstance(inv, InventoryComponent)
    assert inv.capacity == 12
    assert isinstance(inv.player_id, str) and len(inv.player_id) > 0

    # Dirty flag set and write_json called for players
    assert system.dirty_players is True
    paths = [p for (p, _) in writes]
    assert system.active_player_path in paths


def test_run_inventory_init_uses_existing_inventory_and_normalizes_invalid_uuid(monkeypatch):
    writes = []
    monkeypatch.setattr(invrun, 'write_json', lambda path, payload: writes.append((path, payload)), raising=True)

    # Pre-existing inventory should not be overwritten
    existing = InventoryComponent(capacity=5, player_id='keep-me')

    system = types.SimpleNamespace(
        active_monsters={},
        active_players={"1": {"player_id": "not-a-uuid", "slots": []}},
        active_neutrals={},
        dirty_monsters=False,
        dirty_players=False,
        dirty_neutrals=False,
        player_template={"capacity": 9},
        schema_version="1.0.0",
        active_monster_path="/dev/null/monsters.json",
        active_player_path="/dev/null/players.json",
        active_neutral_path="/dev/null/neutrals.json",
        initialized=set(),
        neutral_templates={},
        monster_templates={},
        vendor_support=types.SimpleNamespace(try_build_inventory_from_seed=lambda *a, **k: None),
    )

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {1: object()},
        'NPCTagComponent': {},
        'MonsterInstanceComponent': {},
        'InventoryComponent': {1: existing},  # existing prevents overwrite
        'ExperienceComponent': {},
    }

    invrun.run_inventory_init_update(system, world)

    # Inventory remains the existing object
    assert world.components['InventoryComponent'][1] is existing

    # When inventory existed, function still normalizes invalid player_id in active_players
    pid = system.active_players["1"].get('player_id')
    # Should be a valid UUID now (or changed); assert parsable UUID
    try:
        uuid.UUID(str(pid))
        valid = True
    except Exception:
        valid = False
    assert valid is True
    assert system.dirty_players is True
