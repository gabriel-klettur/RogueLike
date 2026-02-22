import json
from pathlib import Path

import roguelike_editors.spawner.services.persistence as P


def test_save_and_load_spawners_roundtrip(tmp_path, monkeypatch):
    templates = tmp_path / "spawners_templates.json"
    monkeypatch.setattr(P.paths, "spawners_path", lambda: str(templates), raising=True)

    # Write with legacy fields and non-int building_id
    data = [
        {
            "id": "s1",
            "template_id": "orc",
            "zone": "forest",
            "tile": [1, 2],
            "spawner_img": "deprecated.png",
            "spawner_img_size": [16, 16],
            "building_id": "7",
        }
    ]
    P.write_spawners_json(data)

    # Load must sanitize legacy keys and normalize building_id
    loaded = P.load_spawners_json()
    assert isinstance(loaded, list) and len(loaded) == 1
    sp = loaded[0]
    assert "spawner_img" not in sp and "spawner_img_size" not in sp
    assert sp.get("building_id") == 7

    # Save/update single template by id (replace)
    P.save_spawner_template({"id": "s1", "template_id": "orc2", "zone": "forest", "tile": [1, 2]})
    after = json.loads(templates.read_text(encoding="utf-8"))
    assert any(t["template_id"] == "orc2" for t in after if t.get("id") == "s1")

    # Save as new (append)
    P.save_spawner_template({"id": "s2", "template_id": "goblin", "zone": "cave", "tile": [3, 4]})
    after2 = json.loads(templates.read_text(encoding="utf-8"))
    ids = {t.get("id") for t in after2}
    assert {"s1", "s2"}.issubset(ids)


def test_instances_write_dedup_and_load_assigns_ids(tmp_path, monkeypatch):
    instances = tmp_path / "spawners_instances.json"
    monkeypatch.setattr(P.paths, "instances_path", lambda: str(instances), raising=True)

    # Two entries with same (template_id, zone, tile) should deduplicate (last wins)
    e1 = {"template_id": "orc", "zone": "forest", "tile": [0, 0], "id": "dup"}
    e2 = {"template_id": "orc", "zone": "forest", "tile": [0, 0], "overrides": {"building_id": "5"}}
    P.write_instances_json([e1, e2])

    raw = json.loads(instances.read_text(encoding="utf-8"))
    assert len(raw) == 1
    # building_id normalized in overrides
    assert raw[0].get("overrides", {}).get("building_id") == 5

    # load_instances_json ensures valid unique ids
    loaded = P.load_instances_json()
    assert isinstance(loaded, list) and len(loaded) == 1
    assert isinstance(loaded[0].get("id"), str) and loaded[0]["id"].strip() != ""
