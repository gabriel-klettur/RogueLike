import importlib
import json


def test_events_module_json_serializable_dir():
    mod = importlib.import_module('roguelike_engine.map.events.events')
    # dir(mod) should be JSON-serializable (list of strings)
    payload = json.dumps(dir(mod))
    assert isinstance(payload, str) and payload.startswith('[')
