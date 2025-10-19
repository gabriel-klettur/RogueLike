import json
from pathlib import Path

from roguelike_ui.services.json_persistence import save_to_json, load_from_json, remove_from_json


def test_save_and_load_roundtrip(tmp_path):
    cfg = tmp_path / "cfg" / "settings.json"
    save_to_json(str(cfg), "volume", 0.8)
    data = load_from_json(str(cfg))
    assert data == {"volume": 0.8}


def test_remove_from_json_success(tmp_path):
    cfg = tmp_path / "cfg.json"
    cfg.write_text(json.dumps({"foo": 1, "bar": 2}), encoding="utf-8")

    ok = remove_from_json(str(cfg), "bar")
    assert ok is True

    data = json.loads(cfg.read_text(encoding="utf-8"))
    assert data == {"foo": 1}
