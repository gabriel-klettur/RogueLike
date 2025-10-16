import json
from pathlib import Path
import roguelike_game.utils.inventory_registry as reg


def test_inventory_registry_persistence_across_runs(tmp_path, monkeypatch):
    root = tmp_path / 'registry'
    monkeypatch.setattr(reg, 'REGISTRY_ROOT', root, raising=True)

    pid = '00000000-0000-0000-0000-00000000abcd'
    snap1 = {
        'player_id': pid,
        'capacity': 3,
        'slots': [{'id': 'potion', 'qty': 1}],
        'schema_version': '1.0.0',
    }
    v1 = reg.publish_inventory(snap1)
    assert v1 is not None and v1[0] == 1

    # Simular "nuevo proceso": reimportar módulo no es necesario; validamos leyendo archivos
    index_path = root / pid / 'index.json'
    assert index_path.exists()
    index = json.loads(index_path.read_text(encoding='utf-8'))
    assert index.get('last_version') == 1
    assert index.get('entries') and index['entries'][-1]['version'] == 1

    # Publicar cambio -> nueva versión v2
    snap2 = {**snap1, 'slots': [{'id': 'potion', 'qty': 2}]}
    v2 = reg.publish_inventory(snap2)
    assert v2 is not None and v2[0] == 2

    # Resolver última versión y versión específica
    latest = reg.resolve_inventory(pid)
    assert latest is not None and latest['slots'] == [{'id': 'potion', 'qty': 2}]
    v1_snapshot = reg.resolve_inventory(pid, version=1)
    assert v1_snapshot is not None and v1_snapshot['slots'] == [{'id': 'potion', 'qty': 1}]
