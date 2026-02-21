import importlib


def test_events_reload_does_not_accumulate_state():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    baseline = set(dir(mod))

    # Reload several times; directory of symbols should remain the same
    for _ in range(5):
        mod = importlib.reload(mod)
        assert set(dir(mod)) == baseline
