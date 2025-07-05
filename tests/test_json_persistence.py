import os
import json
import pytest
from pathlib import Path
from roguelike_ui.services.json_persistence import save_to_json, load_from_json

def test_load_nonexistent(tmp_path):
    path = tmp_path / "test.json"
    result = load_from_json(str(path))
    assert result == {}

def test_save_and_load(tmp_path):
    path = tmp_path / "test.json"
    data1 = {"a": 1}
    save_to_json(str(path), "key1", data1)
    assert Path(path).exists(), "JSON file should be created"
    with open(path, encoding="utf-8") as f:
        raw = json.load(f)
    assert raw.get("key1") == data1
    loaded = load_from_json(str(path))
    assert loaded.get("key1") == data1

def test_overwrite_key(tmp_path):
    path = tmp_path / "test.json"
    save_to_json(str(path), "k", "first")
    save_to_json(str(path), "k", "second")
    loaded = load_from_json(str(path))
    assert loaded.get("k") == "second"
