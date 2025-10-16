import importlib


def test_events_module_has_no_tiles_coupling():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    # Ensure module does not import tile-related modules implicitly
    names = set(dir(mod))
    # Just a sanity check: module should be effectively empty
    public = [n for n in names if not n.startswith('_')]
    assert public == []
