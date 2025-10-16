import importlib
import pytest


def test_importing_nonexistent_submodule_raises():
    with pytest.raises(ModuleNotFoundError):
        importlib.import_module('roguelike_engine.map.events.nonexistent')


def test_calling_missing_handler_raises():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    with pytest.raises(AttributeError):
        getattr(mod, 'handle_event')()
