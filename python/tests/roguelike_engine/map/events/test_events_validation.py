import importlib
import pytest


def test_events_no_public_symbols():
    pkg = importlib.import_module('roguelike_engine.map.events')
    for name in ('on_click', 'on_key', 'dispatch'):
        assert not hasattr(pkg, name)


def test_importing_unknown_symbol_raises():
    pkg = importlib.import_module('roguelike_engine.map.events')
    with pytest.raises(AttributeError):
        getattr(pkg, 'non_existent_symbol')
