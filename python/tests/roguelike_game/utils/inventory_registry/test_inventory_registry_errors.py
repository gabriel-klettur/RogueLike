import json
from pathlib import Path

import roguelike_game.utils.inventory_registry as reg


def test_publish_inventory_rejects_invalid_uuid(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    snap = {
        'player_id': 'not-a-uuid',  # invalid -> should be rejected
        'capacity': 5,
        'slots': [],
    }
    assert reg.publish_inventory(snap) is None


def test_resolve_inventory_missing_index_returns_none(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    # No index.json present
    assert reg.resolve_inventory('00000000-0000-0000-0000-000000000111') is None


def test_resolve_inventory_bad_index_returns_none(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    pid = '00000000-0000-0000-0000-000000000222'
    pid_dir = tmp_path / 'registry' / pid
    pid_dir.mkdir(parents=True)
    (pid_dir / 'index.json').write_text('{ invalid', encoding='utf-8')

    assert reg.resolve_inventory(pid) is None
