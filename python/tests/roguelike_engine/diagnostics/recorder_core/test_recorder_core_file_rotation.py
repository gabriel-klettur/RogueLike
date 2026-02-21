import json
import os
import tempfile

from roguelike_engine.diagnostics.recorder_core.writer import write_session


def test_write_session_creates_distinct_files_by_started_ts(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        # Route logs under tmp
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        data1 = {"_started_ts": 1_700_000_001.0, "session_id": "A"}
        data2 = {"_started_ts": 1_700_000_002.0, "session_id": "B"}
        p1 = write_session(dict(data1))
        p2 = write_session(dict(data2))
        assert os.path.exists(p1) and os.path.exists(p2)
        assert p1 != p2
        with open(p1, "r", encoding="utf-8") as f:
            j1 = json.load(f)
        with open(p2, "r", encoding="utf-8") as f:
            j2 = json.load(f)
        assert j1.get("session_id") == "A"
        assert j2.get("session_id") == "B"
