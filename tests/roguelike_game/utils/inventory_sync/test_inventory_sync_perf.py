import time
import uuid
import json
import roguelike_game.utils.inventory_sync as invsync


def test_inventory_sync_perf(tmp_path, monkeypatch):
    # Redirige salida a tmp y mide tiempo de 120 escrituras
    active = tmp_path / 'inventory' / 'active' / 'inventory_player.json'
    monkeypatch.setattr(invsync, 'ACTIVE_PATH', active, raising=True)

    base_pid = str(uuid.uuid4())
    t0 = time.perf_counter()
    for i in range(120):
        snap = {
            'player_id': base_pid,  # mismo jugador
            'capacity': 10 + (i % 3),  # pequeñas variaciones
            'slots': [{'id': 'coin', 'qty': i % 7}],
            'schema_version': '1.0.0',
        }
        invsync.write_active_for_player(i, snap)
    dt = time.perf_counter() - t0

    # Debe ejecutarse con holgura en menos de 0.5s en CI típico
    assert dt < 0.5

    # Persistencia: archivo creado y con 120 entradas
    assert active.exists()
    data = json.loads(active.read_text(encoding='utf-8'))
    assert len(data) == 120
