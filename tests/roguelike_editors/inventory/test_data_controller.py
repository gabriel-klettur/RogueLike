import pytest
from types import SimpleNamespace
import os, builtins, io
import roguelike_editors.inventory.data_controller as dc_mod
from roguelike_editors.inventory.data_controller import DataController


def test_load_data_populates_model(monkeypatch, tmp_path):
    # Stub load_from_json
    def fake_load(path):
        return {'value': path}
    monkeypatch.setattr(dc_mod, 'load_from_json', fake_load)
    # Stub os.makedirs to no-op
    monkeypatch.setattr(os, 'makedirs', lambda *args, **kwargs: None)
    # Stub open to write to memory
    monkeypatch.setattr(builtins, 'open', lambda *args, **kwargs: io.StringIO())
    # Stub import jsonschema to raise ImportError
    orig_import = builtins.__import__
    def fake_import(name, globals=None, locals=None, fromlist=(), level=0):
        if name == 'jsonschema':
            raise ImportError
        return orig_import(name, globals, locals, fromlist, level)
    monkeypatch.setattr(builtins, '__import__', fake_import)
    # Prepare model
    model = SimpleNamespace(default_data={}, active_data={})
    ctrl = DataController(model)
    # Override paths to use tmp_path to avoid real files
    for cat in ctrl.paths:
        ctrl.paths[cat] = {'default': str(tmp_path/f"{cat}_d.json"), 'active': str(tmp_path/f"{cat}_a.json")}    
    ctrl.load_data()
    assert set(model.default_data.keys()) == set(ctrl.paths.keys())
    assert set(model.active_data.keys()) == set(ctrl.paths.keys())
    for cat, p in ctrl.paths.items():
        assert model.default_data[cat] == {'value': p['default']}
        assert model.active_data[cat] == {'value': p['active']}


def test_nested_map_active_data_key_extracted(monkeypatch, tmp_path):
    # Stub load_from_json for nested map only on map active
    def fake_load(path):
        if path.endswith('map_a.json'):
            return {'map': {'k': 'v'}}
        if path.endswith('_a.json'):
            return {'active': True}
        return {'default': True}
    monkeypatch.setattr(dc_mod, 'load_from_json', fake_load)

    monkeypatch.setattr(os, 'makedirs', lambda *args, **kwargs: None)
    monkeypatch.setattr(builtins, 'open', lambda *args, **kwargs: io.StringIO())
    orig_import = builtins.__import__
    # Skip jsonschema
    monkeypatch.setattr(builtins, '__import__', lambda name, *args, **kwargs: (_ for _ in ()).throw(ImportError) if name=='jsonschema' else orig_import(name, *args, **kwargs))
    # Setup model and controller
    model = SimpleNamespace(default_data={}, active_data={})
    ctrl = DataController(model)
    # Override paths
    for cat in ctrl.paths:
        ctrl.paths[cat] = {'default': str(tmp_path/f"{cat}_d.json"), 'active': str(tmp_path/f"{cat}_a.json")}    
    ctrl.load_data()
    # 'map' active should extract nested
    assert model.active_data['map'] == {'k': 'v'}
    # Others should be the 'active' stub
    for cat in ('player', 'monsters'):
        assert model.active_data[cat] == {'active': True}  # stub logic not applicable
