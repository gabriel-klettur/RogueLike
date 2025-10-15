import json
from pathlib import Path

import roguelike_game.utils.inventory_registry as reg


def test_inventory_registry_lookup_latest_and_specific(tmp_path, monkeypatch):
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', tmp_path / 'registry', raising=True)

    pid = '00000000-0000-0000-0000-000000000abc'
    snap1 = {
        'player_id': pid,
        'capacity': 3,
        'slots': [{'id': 'wood', 'qty': 1}],
    }
    snap2 = {
        'player_id': pid,
        'capacity': 3,
        'slots': [{'id': 'wood', 'qty': 2}],
    }

    reg.publish_inventory(snap1)
    reg.publish_inventory(snap2)

    # Latest version should be v2
    latest = reg.resolve_inventory(pid)
    assert latest is not None
    assert latest['slots'] == [{'id': 'wood', 'qty': 2}]

    # Specific version should read v1
    v1 = reg.resolve_inventory(pid, version=1)
    assert v1 is not None
    assert v1['slots'] == [{'id': 'wood', 'qty': 1}]
