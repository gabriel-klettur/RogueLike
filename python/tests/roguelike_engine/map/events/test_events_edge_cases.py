import importlib
import pytest


def test_getattr_missing_raises_attribute_error():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    with pytest.raises(AttributeError):
        getattr(mod, 'nonexistent_handler')


def test_from_import_missing_raises():
    with pytest.raises(ModuleNotFoundError):
        importlib.import_module('roguelike_engine.map.events.missing')
