import json
import os
from pathlib import Path

import pytest

import roguelike_engine.zone.zone_controller as zc
from roguelike_engine.zone.zone_controller import ZonesService
from roguelike_engine.config import config as cfg
from roguelike_engine.config.map_config import global_map_settings


@pytest.fixture()
def temp_data_dir(tmp_path, monkeypatch):
    # Point DATA_DIR to a temporary directory
    monkeypatch.setattr(cfg, "DATA_DIR", str(tmp_path), raising=False)
    # Patch ZonesService module to use temp DATA_DIR too (file ops)
    monkeypatch.setattr(zc, "DATA_DIR", str(tmp_path), raising=False)
    # Repoint ZONES_INDEX to the temp DATA_DIR
    zi = Path(tmp_path) / "map" / "zones" / "zones.json"
    zi.parent.mkdir(parents=True, exist_ok=True)
    monkeypatch.setattr(global_map_settings, "ZONES_INDEX", zi, raising=False)
    # Force JSON mode and reset cached zone_offsets
    monkeypatch.setattr(global_map_settings, "use_zones_json", True, raising=False)
    global_map_settings.__dict__.pop("zone_offsets", None)
    return zi


def _write_zones(zi: Path, data: dict):
    zi.parent.mkdir(parents=True, exist_ok=True)
    zi.write_text(json.dumps(data, indent=2), encoding="utf-8")
    # Invalidate cached offsets
    global_map_settings.__dict__.pop("zone_offsets", None)


def _read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_move_zone_ignores_sentinel_and_moves_regular(temp_data_dir):
    zi = temp_data_dir
    # Create base zones
    _write_zones(zi, {
        "lobby": [0, 0],
        "dungeon": [50, 0],
        "z1": [100, 0],
    })
    svc = ZonesService()

    before = dict(global_map_settings.zone_offsets)
    # Move sentinel must be ignored
    svc.move_zone("no zone", 10, 10)
    assert global_map_settings.zone_offsets["lobby"] == before["lobby"]
    assert global_map_settings.zone_offsets["z1"] == before["z1"]

    # Move regular zone
    svc.move_zone("z1", 5, -3)
    assert global_map_settings.zone_offsets["z1"] == (before["z1"][0] + 5, before["z1"][1] - 3)


def test_add_zone_writes_json_and_updates_offsets(temp_data_dir):
    zi = temp_data_dir
    _write_zones(zi, {
        "lobby": [0, 0],
        "dungeon": [50, 0],
    })
    svc = ZonesService()

    new_name = svc.add_zone_at_tile(125, 75)  # zone size default 50 -> (100,50)
    data = _read_json(zi)
    assert new_name in data
    assert data[new_name] == [100, 50]

    # Offsets cache invalidated by service; ensure new zone present
    assert new_name in global_map_settings.zone_offsets
    assert global_map_settings.zone_offsets[new_name] == (100, 50)


def test_duplicate_rename_delete_with_files(temp_data_dir, tmp_path):
    zi = temp_data_dir
    _write_zones(zi, {
        "lobby": [0, 0],
        "dungeon": [50, 0],
        "z1": [100, 0],
    })
    svc = ZonesService()

    # Duplicate z1
    dup = svc.duplicate_zone("z1")
    assert dup is not None and dup.startswith("z1") and dup != "z1"
    data = _read_json(zi)
    assert dup in data

    # Prepare files for rename/delete
    coll = Path(cfg.DATA_DIR) / "map" / "collisions" / "z1.json"
    over = Path(cfg.DATA_DIR) / "map" / "zones" / "overlays" / "z1.overlay.json"
    coll.parent.mkdir(parents=True, exist_ok=True)
    over.parent.mkdir(parents=True, exist_ok=True)
    coll.write_text("{}", encoding="utf-8")
    over.write_text("{}", encoding="utf-8")

    # Rename z1 -> z1_renamed
    ok = svc.rename_zone("z1", "z1_renamed")
    assert ok
    data = _read_json(zi)
    assert "z1" not in data
    assert "z1_renamed" in data
    # Files renamed
    assert not coll.exists()
    assert not over.exists()
    coll2 = Path(cfg.DATA_DIR) / "map" / "collisions" / "z1_renamed.json"
    over2 = Path(cfg.DATA_DIR) / "map" / "zones" / "overlays" / "z1_renamed.overlay.json"
    assert coll2.exists()
    assert over2.exists()

    # Delete renamed zone
    ok = svc.delete_zone("z1_renamed")
    assert ok
    data = _read_json(zi)
    assert "z1_renamed" not in data
    assert not coll2.exists()
    assert not over2.exists()

    # Cannot delete lobby
    assert svc.delete_zone("lobby") is False


def test_save_zones_filters_sentinel(temp_data_dir):
    zi = temp_data_dir
    _write_zones(zi, {
        "lobby": [0, 0],
        "dungeon": [50, 0],
    })
    svc = ZonesService()
    # Force load and ensure sentinel present in runtime mapping
    _ = global_map_settings.zone_offsets
    assert "no zone" in global_map_settings.zone_offsets
    svc.save_zones()
    data = _read_json(zi)
    assert "no zone" not in data and "no-zone" not in data


def test_load_layers_save_layers_sentinel_guard(monkeypatch):
    svc = ZonesService()
    # load: sentinel guarded
    assert svc.load_layers("no zone") == {}

    calls = {"save": [], "load": []}
    # Patch overlay_manager functions used by service
    import roguelike_engine.map.model.overlay.overlay_manager as om
    monkeypatch.setattr(om, "load_layers", lambda name: calls["load"].append(name) or {"ok": True})
    monkeypatch.setattr(om, "save_layers", lambda name, layers: calls["save"].append((name, layers)))

    out = svc.load_layers("z1")
    assert out == {"ok": True}
    svc.save_layers("no-zone", {"ignored": True})  # sentinel: no-op
    svc.save_layers("z1", {"L": [[""]]})

    assert calls["load"] == ["z1"]
    assert calls["save"] == [("z1", {"L": [[""]]} )]
