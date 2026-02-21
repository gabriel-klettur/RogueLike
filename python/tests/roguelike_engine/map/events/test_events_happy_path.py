def test_events_package_exists_and_empty_api():
    import importlib
    pkg = importlib.import_module('roguelike_engine.map.events')
    assert hasattr(pkg, '__all__')
    assert pkg.__all__ == []

    # events module is present but currently empty by design
    mod = importlib.import_module('roguelike_engine.map.events.events')
    # The module should import without attributes beyond defaults
    exposed = [n for n in dir(mod) if not n.startswith('__')]
    assert exposed == []
