import json
from pathlib import Path

import roguelike_editors.spawner.services.persistence as P


def test_load_spawners_handles_missing_and_invalid(tmp_path, monkeypatch):
    tpl = tmp_path / "spawners_templates.json"
    monkeypatch.setattr(P.paths, "spawners_path", lambda: str(tpl), raising=True)

    # Missing file -> []
    assert P.load_spawners_json() == []

    # Invalid JSON -> []
    tpl.write_text("{ not: 'json' }", encoding="utf-8")
    assert P.load_spawners_json() == []

    # Non-list JSON -> []
    tpl.write_text(json.dumps({"a": 1}), encoding="utf-8")
    assert P.load_spawners_json() == []


def test_write_spawners_normalizes_fields_and_types(tmp_path, monkeypatch):
    tpl = tmp_path / "spawners_templates.json"
    monkeypatch.setattr(P.paths, "spawners_path", lambda: str(tpl), raising=True)

    data = [
        {"id": "x", "spawner_img": "x.png", "spawner_img_size": [8, 8], "building_id": "3"},
        "not-a-dict",
    ]
    P.write_spawners_json(data)

    saved = json.loads(tpl.read_text(encoding="utf-8"))
    assert isinstance(saved, list)
    assert all(isinstance(e, dict) for e in saved)
    assert "spawner_img" not in saved[0] and "spawner_img_size" not in saved[0]
    assert saved[0].get("building_id") == 3


ess = [
    {"template_id": "a", "zone": "z", "tile": [0, 0], "id": "dup"},
    {"template_id": "a", "zone": "z", "tile": [0, 0], "id": "dup"},
]

def test_load_instances_assigns_unique_ids(tmp_path, monkeypatch):
    inst = tmp_path / "spawners_instances.json"
    monkeypatch.setattr(P.paths, "instances_path", lambda: str(inst), raising=True)

    # Persist duplicates
    inst.write_text(json.dumps(ess, ensure_ascii=False), encoding="utf-8")

    loaded = P.load_instances_json()
    assert isinstance(loaded, list) and len(loaded) == 2  # second pass regenerates id for duplicate
    ids = [e.get("id") for e in loaded]
    assert len(set(ids)) == len(ids)
