import types

import roguelike_game.ecs.systems.inventory.inventory_update_runner as invrun


def test_run_inventory_init_no_players_no_npcs_no_writes(monkeypatch, tmp_path):
    writes = []
    monkeypatch.setattr(invrun, 'write_json', lambda path, payload: writes.append((path, payload)), raising=True)

    system = types.SimpleNamespace(
        active_monsters={},
        active_players={},
        active_neutrals={},
        dirty_monsters=False,
        dirty_players=False,
        dirty_neutrals=False,
        player_template={},
        schema_version="1.0.0",
        active_monster_path=str(tmp_path / 'monsters.json'),
        active_player_path=str(tmp_path / 'players.json'),
        active_neutral_path=str(tmp_path / 'neutrals.json'),
        initialized=set(),
        neutral_templates={},
        monster_templates={},
        vendor_support=types.SimpleNamespace(try_build_inventory_from_seed=lambda *a, **k: None),
    )

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {},
        'NPCTagComponent': {},
        'MonsterInstanceComponent': {},
        'InventoryComponent': {},
        'ExperienceComponent': {},
    }

    invrun.run_inventory_init_update(system, world)

    # No flags and no writes expected
    assert system.dirty_players is False
    assert system.dirty_monsters is False
    assert system.dirty_neutrals is False
    assert writes == []


def test_run_inventory_init_uses_world_snapshot_with_malformed_quantities(monkeypatch, tmp_path):
    writes = []
    monkeypatch.setattr(invrun, 'write_json', lambda path, payload: writes.append((path, payload)), raising=True)

    system = types.SimpleNamespace(
        active_monsters={},
        active_players={},
        active_neutrals={},
        dirty_monsters=False,
        dirty_players=False,
        dirty_neutrals=False,
        player_template={},
        schema_version="1.0.0",
        active_monster_path=str(tmp_path / 'monsters.json'),
        active_player_path=str(tmp_path / 'players.json'),
        active_neutral_path=str(tmp_path / 'neutrals.json'),
        initialized=set(),
        neutral_templates={},
        monster_templates={},
        vendor_support=types.SimpleNamespace(try_build_inventory_from_seed=lambda *a, **k: None),
    )

    world = types.SimpleNamespace()
    world.components = {
        'PlayerTagComponent': {},
        'NPCTagComponent': {2: object()},
        'MonsterInstanceComponent': {2: types.SimpleNamespace(instance_id='inst-2')},
        'InventoryComponent': {},
        'ExperienceComponent': {},
        # Provide snapshot with non-integer quantity to hit conversion path
        'NPCInventorySnapshot': {
            'inst-2': {
                'template_id': 'orc',
                'slots': [
                    {'item': 'coin', 'quantity': '10'},  # string quantity
                    {'item': 'potion', 'quantity': None},
                ]
            }
        }
    }

    invrun.run_inventory_init_update(system, world)

    # Should mark monsters dirty and write active_monster_path
    assert system.dirty_monsters is True
    paths = [p for (p, _) in writes]
    assert system.active_monster_path in paths
