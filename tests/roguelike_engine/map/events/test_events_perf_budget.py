import importlib
import time


def test_events_import_perf_budget():
    start = time.perf_counter()
    mod = importlib.import_module('roguelike_engine.map.events.events')
    elapsed_ms = (time.perf_counter() - start) * 1000.0
    # Generous budget to avoid flakiness on CI
    assert elapsed_ms < 250.0
    assert mod is not None
