import json
from pathlib import Path

import pytest

import roguelike_game.utils.inventory_sync as invsync


def test_content_hash_stable_and_canonical():
    s1 = {
        'player_id': '00000000-0000-0000-0000-000000000001',
        'capacity': 30,
        'slots': [{'id': 'potion', 'qty': 1}],
        'schema_version': '1.0.0',
        'ignored': 'x',
    }
    s2 = {
        # re-ordered keys and extra fields should not change canonical hash
        'schema_version': '1.0.0',
        'slots': [{'qty': 1, 'id': 'potion'}],
        'player_id': '00000000-0000-0000-0000-000000000001',
        'capacity': 30,
        'extra': {'k': 1},
    }
    h1 = invsync.content_hash(s1)
    h2 = invsync.content_hash(s2)
    assert h1 == h2
    assert isinstance(h1, str) and len(h1) == 64


def test_write_and_read_active_monkeypatched_path(tmp_path, monkeypatch):
    # Redirect ACTIVE_PATH to a temp file to avoid touching repo state
    active = tmp_path / 'inventory' / 'active' / 'inventory_player.json'
    monkeypatch.setattr(invsync, 'ACTIVE_PATH', active, raising=True)

    # entity_id can be any hashable; player_id invalid -> will be replaced with uuid
    snapshot = {
        'player_id': 'not-a-uuid',
        'capacity': 10,
        'slots': [{'id': 'coin', 'qty': 5}],
    }
    invsync.write_active_for_player(42, snapshot)

    assert active.exists()
    data = json.loads(active.read_text(encoding='utf-8'))
    assert '42' in data
    entry = data['42']
    assert entry['capacity'] == 10
    assert entry['slots'] == [{'id': 'coin', 'qty': 5}]
    # auto-filled schema_version
    assert entry['schema_version'] == '1.0.0'

    # No rewrite when no logical changes
    before = active.read_text(encoding='utf-8')
    invsync.write_active_for_player(42, entry)
    after = active.read_text(encoding='utf-8')
    assert before == after

    # read_active_for_player returns the same entry
    got = invsync.read_active_for_player(42)
    assert got == entry
