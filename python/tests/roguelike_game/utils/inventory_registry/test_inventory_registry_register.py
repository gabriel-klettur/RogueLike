import json
from pathlib import Path

import pytest

import roguelike_game.utils.inventory_registry as reg


def test_publish_inventory_versions_and_dedup(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    snap = {
        'player_id': '00000000-0000-0000-0000-000000000123',
        'capacity': 5,
        'slots': [{'id': 'apple', 'qty': 2}],
        'schema_version': '1.0.0',
    }
    v1 = reg.publish_inventory(snap)
    assert v1 is not None
    version, h1, p1 = v1
    assert version == 1
    assert Path(p1).exists()

    # Publishing same logical snapshot should deduplicate and keep version 1
    v2 = reg.publish_inventory({**snap, 'ignored': True})
    assert v2 is not None
    version2, h2, p2 = v2
    assert version2 == 1
    assert h2 == h1
    assert p2.endswith('v1.json')

    # Change content -> new version
    changed = {**snap, 'slots': [{'id': 'apple', 'qty': 3}]}
    v3 = reg.publish_inventory(changed)
    assert v3 is not None
    version3, h3, p3 = v3
    assert version3 == 2
    assert h3 != h1
    assert Path(p3).exists()


def test_resolve_inventory_latest_and_specific(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    snap = {
        'player_id': '00000000-0000-0000-0000-000000000999',
        'capacity': 2,
        'slots': [{'id': 'coin', 'qty': 10}],
    }
    reg.publish_inventory(snap)
    reg.publish_inventory({**snap, 'slots': [{'id': 'coin', 'qty': 11}]})

    latest = reg.resolve_inventory('00000000-0000-0000-0000-000000000999')
    assert latest is not None
    assert latest['slots'] == [{'id': 'coin', 'qty': 11}]

    v1 = reg.resolve_inventory('00000000-0000-0000-0000-000000000999', version=1)
    assert v1 is not None
    assert v1['slots'] == [{'id': 'coin', 'qty': 10}]
