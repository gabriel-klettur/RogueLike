import json
import pytest

import roguelike_editors.lighting.services.light_instances_service as svc


@pytest.fixture()
def temp_paths(tmp_path, monkeypatch):
    inst = tmp_path / "light_instances.json"
    presets = tmp_path / "presets.json"
    monkeypatch.setattr(svc, "LIGHT_INSTANCES_PATH", str(inst), raising=True)
    monkeypatch.setattr(svc, "LIGHT_PRESETS_PATH", str(presets), raising=True)
    return inst, presets


def test_delete_instances_empty_and_nonexistent(temp_paths):
    inst_path, _ = temp_paths
    # Seed with two entries
    data = [
        {"id": 1, "preset_id": "torch", "zone": "z0", "rel_x": 0, "rel_y": 0},
        {"id": 2, "preset_id": "torch", "zone": "z0", "rel_x": 1, "rel_y": 1},
    ]
    svc.write_light_instances(data)
    # Empty set => 0
    assert svc.delete_instances(set()) == 0
    # Nonexistent ids => 0
    assert svc.delete_instances({99, 100}) == 0
    # Ensure unchanged
    left = svc.load_light_instances()
    assert {e["id"] for e in left} == {1, 2}


def test_write_light_instances_dedup_large(temp_paths):
    inst_path, _ = temp_paths
    base = {"preset_id": "torch", "zone": "z0", "rel_x": 5, "rel_y": 7}
    data = []
    # 200 duplicates of the same signature, and 50 different (note: i=0 collides with base)
    for i in range(200):
        d = dict(base)
        d["id"] = i + 1
        data.append(d)
    for i in range(50):
        data.append({"id": 1000 + i, "preset_id": "torch", "zone": "z0", "rel_x": 5 + i, "rel_y": 7})
    svc.write_light_instances(data)
    out = svc.load_light_instances()
    # Dedup leaves 1 of the duplicates + 49 unique (since i=0 duplicates base) => 50 total
    assert len(out) == 50
    # Ensure only one entry with rel_x==5,rel_y==7 remains (dedup by signature)
    base_hits = [e for e in out if e.get("rel_x") == 5 and e.get("rel_y") == 7 and e.get("preset_id") == "torch"]
    assert len(base_hits) == 1
    # Ensure sorted by id ascending
    ids = [e.get("id", 0) for e in out]
    assert ids == sorted(ids)
