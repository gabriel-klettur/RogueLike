import json
from pathlib import Path

from roguelike_ui.services.json_persistence import save_to_json, load_from_json, remove_from_json


def test_load_returns_empty_on_missing_or_invalid(tmp_path):
    # Missing file -> {}
    missing = tmp_path / "missing.json"
    assert load_from_json(str(missing)) == {}

    # Invalid JSON -> {}
    bad = tmp_path / "bad.json"
    bad.write_text("{ this is not json }", encoding="utf-8")
    assert load_from_json(str(bad)) == {}


def test_remove_from_json_missing_or_invalid(tmp_path):
    # Missing file -> False
    missing = tmp_path / "missing.json"
    assert remove_from_json(str(missing), "any") is False

    # Invalid JSON -> False
    bad = tmp_path / "bad.json"
    bad.write_text("{ not: 'json' }", encoding="utf-8")
    assert remove_from_json(str(bad), "k") is False


essential = {"a": 1}

def test_save_to_json_handles_corrupted_existing(tmp_path):
    # Pre-create file with invalid JSON; save_to_json should overwrite safely
    corrupted = tmp_path / "cfg.json"
    corrupted.write_text("{ invalid", encoding="utf-8")

    save_to_json(str(corrupted), "volume", 0.5)
    data = json.loads(corrupted.read_text(encoding="utf-8"))
    assert data == {"volume": 0.5}
