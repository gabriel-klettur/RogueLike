import time
import os
import tempfile

from roguelike_engine.diagnostics.recorder_core.writer import write_summary
from roguelike_engine.diagnostics.recorder_core.aggregator import MetricsAggregator


def test_write_summary_performance_with_many_metrics(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        data = {
            "session_id": "diag_perf",
            "started_at": "2025-01-01T00:00:00Z",
            "ended_at": "2025-01-01T00:00:10Z",
            "duration_seconds": 10.0,
            "game_context": {"map_name": "X"},
        }
        agg = MetricsAggregator()
        # Simulate many metrics streams
        for i in range(1000):
            key = f"{i}.Metric {i}"
            for _ in range(10):
                agg.update_metric(key, (i % 7 + 1) * 0.5)
        for v in [30.0, 60.0, 120.0]:
            agg.update_fps(v)
            agg.update_frame_time(1000.0 / v)

        t0 = time.perf_counter()
        json_path, log_path = write_summary(data, agg)
        dt = time.perf_counter() - t0
        assert os.path.exists(json_path)
        assert os.path.exists(log_path)
        assert dt < 2.0
