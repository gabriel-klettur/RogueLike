import json
import os
import tempfile
import threading
import time

from roguelike_engine.diagnostics.recorder_core.writer import write_session


def test_concurrent_write_session_produces_valid_json_files(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setattr("os.getcwd", lambda: tmp)

        outputs = []

        def worker(start_ts: float, sid: str):
            data = {"_started_ts": start_ts, "session_id": sid}
            path = write_session(dict(data))
            outputs.append((path, sid))

        # Two threads with distinct timestamps to avoid name collision
        t1 = threading.Thread(target=worker, args=(1_700_000_001.0, "A"))
        t2 = threading.Thread(target=worker, args=(1_700_000_002.0, "B"))
        t1.start(); t2.start(); t1.join(); t2.join()

        assert len(outputs) == 2
        for path, sid in outputs:
            assert os.path.exists(path)
            with open(path, "r", encoding="utf-8") as f:
                data = json.load(f)
            assert data.get("session_id") == sid
