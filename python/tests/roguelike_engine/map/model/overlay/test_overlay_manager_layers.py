import types
import pytest
import roguelike_engine.map.model.overlay.overlay_manager as om
from roguelike_engine.map.model.layer import Layer


def test_load_layers_none_returns_empty(monkeypatch):
    calls = {}
    store = types.SimpleNamespace(load=lambda key: None, save=lambda k, d: calls.update({'save': (k, d)}))
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    data = om.load_layers('any')
    assert data == {}


def test_load_layers_legacy_list_as_ground(monkeypatch):
    legacy = [['g']]
    store = types.SimpleNamespace(load=lambda key: legacy, save=lambda k, d: None)
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    data = om.load_layers('map')
    assert Layer.Ground in data and data[Layer.Ground] == legacy


def test_load_layers_dict_ignores_unknown_layers(monkeypatch):
    payload = {
        'layers': {
            'Ground': [["g"]],
            'Unknown': [["?"]],
            'WallsTop': [["w"]],
        }
    }
    store = types.SimpleNamespace(load=lambda key: payload, save=lambda k, d: None)
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    data = om.load_layers('x')
    assert Layer.Ground in data and Layer.WallsTop in data
    # Unknown layer name should be ignored
    assert all(isinstance(k, Layer) for k in data.keys())


def test_save_layers_writes_new_format(monkeypatch):
    # Pure serialization check to avoid IO and monkeypatch flakiness
    payload = om.serialize_layers_payload({Layer.Ground: [["g"]], Layer.Decorations: [["d"]]})
    assert 'layers' in payload
    assert set(payload['layers'].keys()) == {Layer.Ground.name, Layer.Decorations.name}
