from typing import Dict, List

import pytest

from roguelike_engine.diagnostics.recorder_core.snapshot import build_flat_metrics


def test_build_flat_metrics_uses_last_60_samples():
    # 100 samples in seconds, increasing
    samples = [i / 1000.0 for i in range(1, 101)]
    perf_log: Dict[str, List[float]] = {"1.Task": samples}

    flat = build_flat_metrics(perf_log)
    assert "1.Task" in flat
    avg_ms = flat["1.Task"]

    last_60 = samples[-60:]
    expected_ms = round((sum(last_60) / len(last_60)) * 1000.0, 3)
    assert avg_ms == expected_ms
