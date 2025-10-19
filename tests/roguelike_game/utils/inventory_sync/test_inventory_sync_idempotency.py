import json
from pathlib import Path

import roguelike_game.utils.inventory_sync as invsync


def test_write_active_idempotency(tmp_path, monkeypatch):
    active = tmp_path / 'inventory' / 'active' / 'inventory_player.json'
    monkeypatch.setattr(invsync, 'ACTIVE_PATH', active, raising=True)

    entry = {
        'player_id': '00000000-0000-0000-0000-000000000001',
        'capacity': 5,
        'slots': [{'id': 'potion', 'qty': 1}],
        'schema_version': '1.0.0',
    }
    invsync.write_active_for_player('player-entity', entry)

    before = active.read_text(encoding='utf-8')
    invsync.write_active_for_player('player-entity', entry)
    after = active.read_text(encoding='utf-8')
    assert before == after
