import json
import os
import tempfile

from roguelike_engine.diagnostics.recorder_core.aggregator import MetricsAggregator
from roguelike_engine.diagnostics.recorder_core.writer import write_summary


def test_write_summary_json_schema_contains_expected_sections(monkeypatch):
    with tempfile.TemporaryDirectory() as tmp:
        monkeypatch.setattr("os.getcwd", lambda: tmp)
        data = {
            "session_id": "diag_123",
            "started_at": "2025-01-01T00:00:00Z",
            "ended_at": "2025-01-01T00:00:05Z",
            "duration_seconds": 5.0,
            "game_context": {"map_name": "map1", "world_level": 2},
        }
        agg = MetricsAggregator()
        agg.update_fps(60.0)
        agg.update_frame_time(16.67)
        for i in range(3):
            agg.update_metric(f"{i}.Sys", 0.5 + i)

        json_path, log_path = write_summary(data, agg)
        with open(json_path, "r", encoding="utf-8") as f:
            payload = json.load(f)

        # Top-level keys
        assert set(["session_id", "started_at", "ended_at", "duration_seconds", "game_context", "fps", "frame_time_ms", "metrics"]).issubset(payload.keys())
        # FPS and FT shapes
        assert set(["avg", "min", "max", "samples"]).issubset(payload["fps"].keys())
        assert set(["avg", "min", "max", "samples"]).issubset(payload["frame_time_ms"].keys())
        # Metrics map has entries for keys
        assert any(k.endswith(".Sys") for k in payload["metrics"].keys())
        # Log table exists
        assert os.path.exists(log_path)
