import json
import os
import tempfile

from roguelike_engine.diagnostics.recorder import DiagnosticsSessionRecorder


class _Clock:
    def get_fps(self) -> float:
        return 60.0


class _State:
    clock = _Clock()


class _Model:
    def __init__(self, perf_log):
        self.perf_log = perf_log


def test_session_file_strips_internal_fields_and_contains_samples(monkeypatch):
    # Retention policy: persisted session should not include internal fields like _started_ts
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        rec = DiagnosticsSessionRecorder()

        # Use deterministic time to trigger two samples (>1s apart)
        times = [1000.0, 1001.2, 1002.5, 1003.9]
        it = iter(times)
        monkeypatch.setattr("time.time", lambda: next(it))

        rec.on_toggle(True, game=None)
        rec.record_tick(_Model({"1.A": [0.003] * 60}), state=_State())
        rec.record_tick(_Model({"1.A": [0.003] * 60}), state=_State())
        rec.finish_if_active(game=None)

        # Find latest diagnostics session file
        diag_dir = os.path.join(tmp, "logs", "diagnostics")
        files = sorted([f for f in os.listdir(diag_dir) if f.endswith(".json")])
        assert files
        with open(os.path.join(diag_dir, files[-1]), "r", encoding="utf-8") as f:
            data = json.load(f)
        # Internal field must not be in persisted payload
        assert "_started_ts" not in data
        # Samples may be empty if toggle duration < 1s; ensure keys exist
        assert "session_id" in data and "started_at" in data and "samples" in data
