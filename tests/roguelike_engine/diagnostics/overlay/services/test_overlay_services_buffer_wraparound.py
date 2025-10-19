from typing import Dict, List

import pytest

from roguelike_engine.diagnostics.overlay.services.perf_tree import build_perf_tree


def test_perf_tree_averages_last_60_samples():
    # Build 100 samples (in seconds), increasing linearly
    samples = [i / 1000.0 for i in range(1, 101)]  # 0.001 .. 0.100
    perf_log: Dict[str, List[float]] = {"1.Task": samples}

    tree = build_perf_tree(perf_log)
    node_1 = tree["children"]["1"]
    # Find the item for id '1'
    items = node_1["items"]
    assert len(items) == 1
    item_id, label, avg_ms = items[0]
    assert item_id == "1"

    # Expected average over last 60 samples (41..100) converted to ms
    last_60 = samples[-60:]
    expected_ms = (sum(last_60) / len(last_60)) * 1000.0
    assert pytest.approx(avg_ms, rel=1e-6) == expected_ms
