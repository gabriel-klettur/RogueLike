import json
from pathlib import Path

import roguelike_engine.map.model.overlay.json_store as js
from roguelike_engine.config import map_config


def test_json_overlay_store_save_and_load_by_zone(tmp_path, monkeypatch):
    # Redirect DATA_DIR for the store to a temporary directory
    monkeypatch.setattr(js, 'DATA_DIR', str(tmp_path), raising=True)

    # Minimal zone offsets so that 'lobby' is recognized as a zone
    class Settings:
        zone_offsets = {'lobby': (0, 0), 'dungeon': (10, 10)}
    monkeypatch.setattr(map_config, 'global_map_settings', Settings, raising=True)

    store = js.JsonOverlayStore()

    overlay = [["a", "b"], ["c", "d"]]
    store.save('lobby', overlay)

    # Verify file exists in overlays directory
    out_dir = Path(tmp_path) / 'map' / 'zones' / 'overlays'
    assert out_dir.is_dir()
    f = out_dir / 'lobby.overlay.json'
    assert f.is_file()

    loaded = store.load('lobby')
    assert loaded == overlay


def test_json_overlay_store_fallback_to_no_zone_when_unknown(tmp_path, monkeypatch):
    monkeypatch.setattr(js, 'DATA_DIR', str(tmp_path), raising=True)

    class Settings:
        zone_offsets = {'lobby': (0, 0)}  # 'unknown' map not present
    monkeypatch.setattr(map_config, 'global_map_settings', Settings, raising=True)

    store = js.JsonOverlayStore()
    overlay = [["x"]]
    store.save('unknown_map', overlay)

    out_dir = Path(tmp_path) / 'map' / 'zones' / 'overlays'
    assert (out_dir / 'no_zone.overlay.json').is_file()

    # Loading unknown should also target 'no_zone'
    loaded = store.load('unknown_map')
    assert loaded == overlay
