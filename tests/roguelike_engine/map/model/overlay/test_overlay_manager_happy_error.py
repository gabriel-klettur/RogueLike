import types

import roguelike_engine.map.model.overlay.overlay_manager as om
from roguelike_engine.map.model.layer import Layer


def test_load_layers_legacy_list_monolayer(monkeypatch):
    # Fake store returning legacy list grid
    class FakeStore:
        def load(self, name):
            return [["a"]]
        def save(self, name, data):
            self.saved = (name, data)
    store = FakeStore()
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    layers = om.load_layers('any')
    assert layers == {Layer.Ground: [["a"]]}


def test_load_layers_new_format_and_ignores_unknown(monkeypatch):
    class FakeStore:
        def load(self, name):
            return {"layers": {
                "Ground": [["g"]],
                "Decorations": [["d"]],
                "UNKNOWN": [["x"]],
            }}
        def save(self, name, data):
            self.saved = (name, data)
    store = FakeStore()
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    layers = om.load_layers('mapx')
    assert Layer.Ground in layers and Layer.Decorations in layers
    assert all(isinstance(k, Layer) for k in layers.keys())
    assert Layer.Ground in layers and layers[Layer.Ground] == [["g"]]


def test_save_layers_serializes_layer_names(monkeypatch):
    recorded = {}
    class FakeStore:
        def load(self, name):
            return None
        def save(self, name, data):
            recorded['name'] = name
            recorded['data'] = data
    store = FakeStore()
    monkeypatch.setattr(om, '_default_store', store, raising=True)

    om.save_layers('m1', {Layer.Ground: [["a"]], Layer.Decorations: [["b"]]})
    assert recorded['name'] == 'm1'
    data = recorded['data']
    assert 'layers' in data and 'Ground' in data['layers'] and 'Decorations' in data['layers']
