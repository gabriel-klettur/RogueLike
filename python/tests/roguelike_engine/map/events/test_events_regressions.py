import importlib


def test_events_pkg_all_stable_across_reload():
    pkg = importlib.import_module('roguelike_engine.map.events')
    before = list(getattr(pkg, '__all__', []))
    for _ in range(3):
        pkg = importlib.reload(pkg)
        assert list(getattr(pkg, '__all__', [])) == before
