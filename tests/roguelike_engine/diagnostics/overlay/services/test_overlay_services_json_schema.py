import json
import os
import tempfile

from roguelike_engine.diagnostics.overlay.services.persistence import (
    save_overlay_state,
    load_overlay_state,
    get_state_file_path,
)


def test_overlay_state_json_shape_contains_collapsed_groups_as_strings():
    with tempfile.TemporaryDirectory() as tmp:
        items = ["1", "2.3", "global", "lobby"]
        save_overlay_state(items, base_path=tmp)
        fp = get_state_file_path(base_path=tmp)
        with open(fp, "r", encoding="utf-8") as f:
            data = json.load(f)
        assert isinstance(data, dict)
        cols = data.get("collapsed_groups")
        assert isinstance(cols, list)
        assert all(isinstance(x, str) for x in cols)
        # Roundtrip via loader
        loaded = load_overlay_state(base_path=tmp)
        assert loaded == sorted(items)
