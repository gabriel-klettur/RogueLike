import json
import threading
import time
import os
import tempfile

from roguelike_engine.diagnostics.overlay.services.persistence import (
    save_overlay_state,
    get_state_file_path,
)


def test_concurrent_save_overlay_state_results_in_valid_json_last_write_wins():
    with tempfile.TemporaryDirectory() as tmp:
        fp = get_state_file_path(base_path=tmp)

        def writer(vals):
            for _ in range(5):
                save_overlay_state(vals, base_path=tmp)
                time.sleep(0.01)

        a = threading.Thread(target=writer, args=(["1", "2"],))
        b = threading.Thread(target=writer, args=(["x", "y", "z"],))
        a.start(); b.start(); a.join(); b.join()

        assert os.path.exists(fp)
        with open(fp, "r", encoding="utf-8") as f:
            data = json.load(f)
        cols = data.get("collapsed_groups")
        # Should be one of the thread-provided lists (sorted by implementation)
        assert cols in (["1", "2"], ["x", "y", "z"]) or cols in (["2", "1"], ["x", "y", "z"]) or cols in (["1", "2"], ["y", "x", "z"])  # tolerant
        assert isinstance(cols, list)
