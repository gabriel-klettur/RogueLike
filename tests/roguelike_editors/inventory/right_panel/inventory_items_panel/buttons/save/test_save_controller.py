import os
import json
import pytest
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_controller import SaveController

@pytest.fixture
def setup_controller(tmp_path):
    # Setup editor controller stub
    model = SimpleNamespace(
        current_category='player',
        default_data={'player': {'slots': [1, 2]}},
        active_data={'player': {'slots': [3, 4]}}
    )
    paths = {
        'player': {
            'default': str(tmp_path / 'default.json'),
            'active': str(tmp_path / 'active.json')
        }
    }
    logs = SimpleNamespace(info_msg=None, error_msg=None)
    def info(msg):
        logs.info_msg = msg
    def error(msg):
        logs.error_msg = msg
    logger = SimpleNamespace(info=info, error=error)
    editor_controller = SimpleNamespace(model=model, paths=paths, logger=logger)
    parent = SimpleNamespace()
    ctrl = SaveController(editor_controller, parent)
    return ctrl, logs, tmp_path, model, paths


def test_save_default_success(setup_controller):
    ctrl, logs, tmp_path, model, paths = setup_controller
    ctrl.save_default()
    filepath = paths['player']['default']
    assert os.path.exists(filepath)
    with open(filepath, 'r', encoding='utf-8') as f:
        data = json.load(f)
    assert data == model.default_data['player']
    assert "saved to" in logs.info_msg
    assert logs.error_msg is None


def test_save_active_success(setup_controller):
    ctrl, logs, tmp_path, model, paths = setup_controller
    ctrl.save_active()
    filepath = paths['player']['active']
    assert os.path.exists(filepath)
    with open(filepath, 'r', encoding='utf-8') as f:
        data = json.load(f)
    assert data == model.active_data['player']
    assert "saved to" in logs.info_msg
    assert logs.error_msg is None


def test_save_default_error(monkeypatch, setup_controller):
    ctrl, logs, tmp_path, model, paths = setup_controller
    monkeypatch.setattr(os, 'makedirs', lambda *args, **kwargs: (_ for _ in ()).throw(Exception("fail")))
    ctrl.save_default()
    assert logs.error_msg is not None
    assert "Error saving default inventory" in logs.error_msg
