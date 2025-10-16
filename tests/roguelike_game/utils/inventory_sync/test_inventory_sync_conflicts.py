import json
import uuid
from pathlib import Path
import roguelike_game.utils.inventory_sync as invsync


def test_inventory_sync_conflicts_overwrite_same_entity(tmp_path, monkeypatch):
    active = tmp_path / 'inventory' / 'active' / 'inventory_player.json'
    monkeypatch.setattr(invsync, 'ACTIVE_PATH', active, raising=True)

    # Initial snapshot for entity 1
    s1 = {
        'player_id': str(uuid.uuid4()),
        'capacity': 10,
        'slots': [{'id': 'coin', 'qty': 1}],
        'schema_version': '1.0.0',
    }
    invsync.write_active_for_player(1, s1)
    before = active.read_text(encoding='utf-8')

    # Different snapshot should overwrite entry 1 (conflict resolution by last write)
    s2 = {
        'player_id': s1['player_id'],  # same identity
        'capacity': 12,  # change capacity to simulate conflict/update
        'slots': [{'id': 'coin', 'qty': 2}],
        'schema_version': '1.0.0',
    }
    invsync.write_active_for_player(1, s2)
    after = active.read_text(encoding='utf-8')

    assert before != after
    data = json.loads(after)
    assert set(data.keys()) == {'1'}
    assert data['1']['capacity'] == 12
    assert data['1']['slots'] == [{'id': 'coin', 'qty': 2}]
