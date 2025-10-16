import importlib


def test_events_compat_schema_public_api_empty():
    pkg = importlib.import_module('roguelike_engine.map.events')
    mod = importlib.import_module('roguelike_engine.map.events.events')

    # Package signals no public API yet
    assert getattr(pkg, '__all__', []) == []

    # Module contains no public names
    public = [n for n in dir(mod) if not n.startswith('_')]
    assert public == []
