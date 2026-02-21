import os
import tempfile

import pytest

from roguelike_engine.diagnostics.recorder import DiagnosticsSessionRecorder


class _Clock:
    def get_fps(self) -> float:
        return 100.0


class _State:
    clock = _Clock()


class _Model:
    def __init__(self, perf_log):
        self.perf_log = perf_log


class _Game:
    def __init__(self, perf_log):
        self.perf_log = perf_log
        self.state = _State()


def test_recorder_lifecycle_start_sample_finish(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        # Keep all writes under tmp via cwd
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        # Avoid writing large benchmark files; just return dummy paths
        monkeypatch.setattr(
            "roguelike_engine.diagnostics.recorder.save_benchmarks",
            lambda benches, base_dir=None: (os.path.join(tmp, "b.json"), os.path.join(tmp, "b.log")),
        )

        rec = DiagnosticsSessionRecorder()

        # Control time.time progression
        times = [1000.0, 1000.2, 1001.3, 1002.6]
        it = iter(times)
        monkeypatch.setattr("time.time", lambda: next(it))

        # Toggle on, then record a few ticks spaced by > 1s only for some calls
        rec.on_toggle(True, game=None)
        rec.record_tick(_Model({"1.A": [0.002] * 60}), state=_State())  # t=1000.2 -> sample
        rec.record_tick(_Model({"1.A": [0.002] * 60}), state=_State())  # t=1001.3 -> skip (dt<1)
        rec.record_tick(_Model({"1.A": [0.002] * 60}), state=_State())  # t=1002.6 -> sample

        # Finish and ensure files are written under tmp
        rec.finish_if_active(game=_Game({"1.A": [0.002] * 60}))
        diag_dir = os.path.join(tmp, "logs", "diagnostics")
        bench_dir = os.path.join(tmp, "logs", "benchmarks")
        assert os.path.isdir(diag_dir)
        # At least one diagnostics file was created
        files = [f for f in os.listdir(diag_dir) if f.endswith('.json')]
        assert files
