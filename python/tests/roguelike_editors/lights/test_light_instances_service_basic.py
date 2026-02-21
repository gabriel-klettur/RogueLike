import json
from typing import Any, Dict

import pytest

# Target module
import roguelike_editors.lighting.services.light_instances_service as svc


@pytest.fixture()
def temp_light_files(tmp_path, monkeypatch):
    inst_path = tmp_path / "light_instances.json"
    presets_path = tmp_path / "presets.json"
    # Point service module constants to temp files
    monkeypatch.setattr(svc, "LIGHT_INSTANCES_PATH", str(inst_path), raising=True)
    monkeypatch.setattr(svc, "LIGHT_PRESETS_PATH", str(presets_path), raising=True)
    # Control tile size for easy math
    monkeypatch.setattr(svc, "TILE_SIZE", 64, raising=True)
    return inst_path, presets_path


@pytest.fixture()
def mock_zone(monkeypatch):
    # Default zone resolver: origin at (0,0) tiles
    def _detect_zone_from_px(x: float, y: float):
        return "z0", (0, 0)

    monkeypatch.setattr(svc, "detect_zone_from_px", _detect_zone_from_px, raising=True)
    return _detect_zone_from_px


def write_presets(presets_path, data: Dict[str, Dict[str, Any]]):
    presets_path.write_text(json.dumps({"presets": data}, ensure_ascii=False, indent=2), encoding="utf-8")


def read_instances(inst_path):
    if not inst_path.exists():
        return []
    return json.loads(inst_path.read_text(encoding="utf-8"))


def test_append_get_update_delete_roundtrip(temp_light_files, mock_zone):
    inst_path, presets_path = temp_light_files
    # Base preset
    base = {
        "radius": 160,
        "intensity": 1.0,
        "falloff": 2.0,
        "color": [255, 200, 140],
        "flicker_amp": 0.0,
        "flicker_speed": 2.3,
        "center_scale": 1.0,
        "enabled": True,
    }
    write_presets(presets_path, {"torch": base})

    # Append with identical params (no overrides expected)
    entry1 = svc.append_instance("torch", 100.0, 200.0, params=base)
    assert entry1["id"] == 1
    assert entry1["zone"] == "z0"
    assert entry1.get("overrides") is None

    # Append with changed color -> overrides contains color only
    mutated = dict(base)
    mutated["color"] = [255, 255, 255]
    entry2 = svc.append_instance("torch", 104.0, 208.0, params=mutated)
    assert entry2["id"] == 2
    assert entry2.get("overrides") == {"color": [255, 255, 255]}

    # Load and get by id
    all_data = svc.load_light_instances()
    assert {e["id"] for e in all_data} == {1, 2}
    got = svc.get_instance_by_id(2)
    assert got and got["id"] == 2 and got["preset_id"] == "torch"

    # Update position crossing zone boundary: change mock to return new zone+offset
    def _detect_zone_from_px_2(x: float, y: float):
        # Pretend zone z1 starts at tile offset (1,0)
        return "z1", (1, 0)

    # Swap mock for update call only
    import importlib

    importlib.reload(svc)  # ensure we keep monkeypatches? We'll reapply below
    # Re-apply monkeypatches after reload
    monkeypatch = pytest.MonkeyPatch()
    monkeypatch.setattr(svc, "LIGHT_INSTANCES_PATH", str(inst_path), raising=True)
    monkeypatch.setattr(svc, "LIGHT_PRESETS_PATH", str(presets_path), raising=True)
    monkeypatch.setattr(svc, "TILE_SIZE", 64, raising=True)
    monkeypatch.setattr(svc, "detect_zone_from_px", _detect_zone_from_px_2, raising=True)

    updated = svc.update_instance_position(1, 130.0, 210.0)
    assert updated and updated["zone"] == "z1"
    # origin at tile 1 => origin_px_x = 1*64 = 64
    assert updated["rel_x"] == int(130 - 64)
    assert updated["rel_y"] == int(210 - 0)

    # Persisted content reflects update
    j = read_instances(inst_path)
    ids = {e["id"] for e in j}
    assert ids == {1, 2}
    e1 = [e for e in j if e["id"] == 1][0]
    assert e1["zone"] == "z1"

    # Delete one
    deleted = svc.delete_instances({1})
    assert deleted == 1
    left = svc.load_light_instances()
    assert len(left) == 1 and left[0]["id"] == 2


def test_write_light_instances_deduplicates_and_sorts(temp_light_files, mock_zone):
    inst_path, _ = temp_light_files
    # Create duplicates differing only by id (should dedup by signature and sort by id when present)
    dup1 = {"id": 10, "preset_id": "torch", "zone": "z0", "rel_x": 0, "rel_y": 0}
    dup2 = {"id": 2, "preset_id": "torch", "zone": "z0", "rel_x": 0, "rel_y": 0}
    different = {"id": 5, "preset_id": "torch", "zone": "z0", "rel_x": 1, "rel_y": 0}

    svc.write_light_instances([dup1, dup2, different])
    data = svc.load_light_instances()
    # Dedup removes one of the duplicates, keep two total
    assert len(data) == 2
    # Sorted by id ascending => id=2 then id=5 or id=10 depending which duplicate kept
    ids_sorted = [e["id"] for e in data]
    assert ids_sorted == sorted(ids_sorted)
