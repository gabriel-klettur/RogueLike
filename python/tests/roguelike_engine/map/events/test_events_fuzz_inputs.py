import importlib
import random
import string
import pytest


def test_events_fuzz_random_attribute_lookups_do_not_crash():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    rng = random.Random(777)
    for _ in range(50):
        name = ''.join(rng.choice(string.ascii_letters + string.digits + '_') for _ in range(rng.randint(1, 20)))
        with pytest.raises(AttributeError):
            getattr(mod, name)


def test_events_fuzz_reload_cycles_no_state_leak():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    baseline = set(dir(mod))
    for _ in range(10):
        mod = importlib.reload(mod)
        assert set(dir(mod)) == baseline
