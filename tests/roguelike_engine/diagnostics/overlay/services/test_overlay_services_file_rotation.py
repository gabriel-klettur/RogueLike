import json
import os
import tempfile

from roguelike_engine.diagnostics.overlay.services.persistence import (
    get_state_file_path,
    load_overlay_state,
    save_overlay_state,
)


def test_persistence_overwrites_state_file_not_accumulates():
    with tempfile.TemporaryDirectory() as tmp:
        # First save
        save_overlay_state(["1", "2", "3"], base_path=tmp)
        fp = get_state_file_path(base_path=tmp)
        assert os.path.exists(fp)
        with open(fp, "r", encoding="utf-8") as f:
            data1 = json.load(f)
        # Second save with different content
        save_overlay_state(["9", "8"], base_path=tmp)
        with open(fp, "r", encoding="utf-8") as f:
            data2 = json.load(f)
        # Should reflect latest content only
        assert data2.get("collapsed_groups") == ["8", "9"]
        assert data1 != data2


def test_load_overlay_state_handles_nonexistent_and_invalid_json():
    with tempfile.TemporaryDirectory() as tmp:
        # Non-existent file -> empty list
        assert load_overlay_state(base_path=tmp) == []
        # Create invalid JSON
        fp = get_state_file_path(base_path=tmp)
        os.makedirs(os.path.dirname(fp), exist_ok=True)
        with open(fp, "w", encoding="utf-8") as f:
            f.write("{ not: valid json }")
        # Should fail silently and return []
        assert load_overlay_state(base_path=tmp) == []
